// Read-only PE inspection; never executes a game function or uses an address cache.
const fs = require('node:fs');
const path = require('node:path');
const [exePath, auditPath, sourceRoot, pattern] = process.argv.slice(2);
const exe = fs.readFileSync(exePath);
const audit = JSON.parse(fs.readFileSync(auditPath, 'utf8'));
const pe = exe.readUInt32LE(0x3c), optional = pe + 24;
const imageBase = Number(exe.readBigUInt64LE(optional + 24));
const imageSize = exe.readUInt32LE(optional + 56);
const sections = [];
for (let i = 0, p = optional + exe.readUInt16LE(pe + 20); i < exe.readUInt16LE(pe + 6); i++, p += 40) {
  sections.push({ name: exe.subarray(p, p + 8).toString().replace(/\0/g, ''),
    rva: exe.readUInt32LE(p + 12), size: exe.readUInt32LE(p + 16), raw: exe.readUInt32LE(p + 20) });
}
const raw = rva => { const s = sections.find(s => rva >= s.rva && rva < s.rva + s.size); if (!s) throw Error(`Unmapped RVA ${rva}`); return rva - s.rva + s.raw; };
const hex = n => '0x' + n.toString(16).toUpperCase();
const scan = signature => {
  const pat = signature.split(' ').map(b => b === '??' ? -1 : parseInt(b, 16));
  const s = sections.find(s => s.name === '.text'), b = exe.subarray(s.raw, s.raw + s.size), hits = [];
  const first = pat.findIndex(x => x !== -1);
  for (let p = first; p < b.length;) {
    p = b.indexOf(pat[first], p); if (p < 0) break;
    const start = p - first; p++;
    if (start + pat.length > b.length) break;
    if (pat.every((x, i) => x === -1 || b[start + i] === x)) hits.push(s.rva + start);
  }
  return hits;
};
if (pattern) {
  console.log(JSON.stringify(scan(pattern).map(rva => ({rva: hex(rva), bytes: exe.subarray(raw(rva), raw(rva)+80).toString('hex')})), null, 2));
  process.exit(0);
}
const vtable = Number(audit.Addresses.find(a => a.Name.endsWith('.UIModule.StaticVirtualTable')).Rva);
const source = fs.readFileSync(path.join(sourceRoot, 'FFXIVClientStructs/FFXIV/Client/UI/UIModuleInterface.cs'), 'utf8').replace(/\/\/[^\n]*/g, '');
const ui = audit.Structures.find(s => s.FullName.endsWith('.UIModule'));
const getters = [];
for (const m of source.matchAll(/\[VirtualFunction\((\d+)\)\]\s+(?:public|internal) partial (\w+)\* (Get\w+)\(\);/g)) {
  const index = Number(m[1]); if (index > 90) continue;
  const target = Number(exe.readBigUInt64LE(raw(vtable) + index * 8)) - imageBase, p = raw(target);
  const offset = exe.subarray(p,p+3).equals(Buffer.from([0x48,0x8d,0x81])) && exe[p+7] === 0xc3 ? exe.readInt32LE(p+3) : null;
  const field = ui.Fields.find(f => f.Name === m[3].slice(3));
  getters.push({name:m[3], rva:hex(target), offset, declared:field?.Offset, matches:field && offset !== null ? offset === field.Offset : null});
}
const duplicates = audit.Addresses.filter(a => a.MatchCount > 1).map(a => ({name:a.Name, targets:a.Matches.map(m => {
  const r = Number(m), p = raw(r), rel = exe[p] === 0xe8 ? 1 : 3;
  return hex(r + rel + 4 + exe.readInt32LE(p + rel));
})}));
const signatures = {
  keyboard:'48 8D 0C 85 ?? ?? ?? ?? 8B 04 39 85 C2 0F 85',
  keyboardIndex:'0F B6 B4 3B ?? ?? ?? ?? 40 84 F6',
  visible:'48 85 C9 74 ?? F7 81 AC 00 00 00 00 00 10 00 74 ?? F7 81 B0 00 00 00 00 00 04 00 75 ?? B8 01 00 00 00 C3 32 C0 C3'
};
const atkVtable = Number(audit.Addresses.find(a => a.Name.endsWith('.RaptureAtkModule.StaticVirtualTable')).Rva);
const atkVirtuals = [4,5,39,40,61].map(index => {
  const target = Number(exe.readBigUInt64LE(raw(atkVtable) + index * 8)) - imageBase;
  return {index, rva:hex(target), bytes:exe.subarray(raw(target),raw(target)+80).toString('hex')};
});
console.log(JSON.stringify({imageSize, getters, atkVirtuals, duplicates,
  outOfImage:audit.Addresses.filter(a=>a.Resolved && (BigInt(a.Rva)<0n || BigInt(a.Rva)>=BigInt(imageSize))).map(a=>({name:a.Name,rva:a.Rva})),
  signatures:Object.fromEntries(Object.entries(signatures).map(([k,v])=>[k,scan(v).map(hex)]))},null,2));
