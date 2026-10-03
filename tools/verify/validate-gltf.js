// Validates .glb/.gltf files with the Khronos glTF validator. Exit code 1 if any file has errors.
// Usage: node tools/verify/validate-gltf.js <file> [more files...]
const fs = require('fs');
const validator = require('gltf-validator');

async function main(files) {
  let failed = false;
  for (const file of files) {
    const report = await validator.validateBytes(new Uint8Array(fs.readFileSync(file)));
    const issues = report.issues;
    console.log(`${file}: errors=${issues.numErrors} warnings=${issues.numWarnings} infos=${issues.numInfos}`);
    for (const m of issues.messages.filter(m => m.severity <= 1).slice(0, 20)) {
      console.log(`  ${m.severity === 0 ? 'ERROR' : 'WARN '} ${m.code} ${m.pointer || ''} ${m.message}`);
    }
    if (issues.numErrors > 0) failed = true;
  }
  process.exit(failed ? 1 : 0);
}

if (process.argv.length < 3) {
  console.error('usage: node validate-gltf.js <file.glb> [...]');
  process.exit(2);
}
main(process.argv.slice(2)).catch(e => { console.error(e); process.exit(2); });
