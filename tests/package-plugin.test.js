import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,copyFile,writeFile,readFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {createHash} from 'node:crypto';
const run=promisify(execFile);
async function fixture(version='0.1.3.0'){
 const root=await mkdtemp(join(tmpdir(),'wf-package-'));await mkdir(join(root,'scripts'));await mkdir(join(root,'assets'));await copyFile(resolve('assets/logo.png'),join(root,'assets/logo.png'));const project=join(root,'plugin/WatermelonFin.Plugin');await mkdir(join(project,'bin/Release/net10.0'),{recursive:true});await copyFile(resolve('scripts/package-plugin.py'),join(root,'scripts/package-plugin.py'));await writeFile(join(project,'WatermelonFin.Plugin.csproj'),`<Project><PropertyGroup><Version>${version}</Version></PropertyGroup></Project>`);await writeFile(join(project,'bin/Release/net10.0/WatermelonFin.Plugin.dll'),'fixture DLL');await writeFile(join(project,'LICENSE'),'fixture license');await writeFile(join(root,'THIRD_PARTY_NOTICES.md'),'fixture notices');return root;
}
test('plugin packaging derives release version from the project and emits matching checksums',async()=>{
 const root=await fixture();const first=JSON.parse((await run('python3',[join(root,'scripts/package-plugin.py')])).stdout);assert.equal(first.version,'0.1.3.0');assert.equal(first.package,'WatermelonFin.Upload-0.1.3.0.zip');assert.equal(first.catalogReady,false);const bytes=await readFile(join(root,'artifacts',first.package));assert.equal(first.sha256,createHash('sha256').update(bytes).digest('hex'));assert.equal((await readFile(join(root,'artifacts',first.package+'.sha256'),'utf8')).trim(),`${first.sha256}  ${first.package}`);const second=JSON.parse((await run('python3',[join(root,'scripts/package-plugin.py')])).stdout);assert.equal(first.sha256,second.sha256);
});
test('catalog metadata uses the same build version without changing the checked-in catalog',async()=>{
 const root=await fixture();await mkdir(join(root,'catalog'));await writeFile(join(root,'catalog/manifest.json'),'original');const result=JSON.parse((await run('python3',[join(root,'scripts/package-plugin.py'),'--source-url','https://fixtures.invalid/WatermelonFin.Upload-0.1.3.0.zip','--image-url','https://fixtures.invalid/watermelonfin-logo.png'])).stdout);const catalog=JSON.parse(await readFile(join(root,'artifacts/manifest.json'),'utf8'));assert.equal(catalog[0].imageUrl,'https://fixtures.invalid/watermelonfin-logo.png');assert.equal(catalog[0].versions[0].version,'0.1.3.0');assert.equal(catalog[0].versions[0].checksum,result.checksum);assert.equal(await readFile(join(root,'catalog/manifest.json'),'utf8'),'original');
});
test('unsafe project versions cannot become output paths',async()=>{
 const root=await fixture('../../outside');await assert.rejects(()=>run('python3',[join(root,'scripts/package-plugin.py')]));
});

test('plugin ZIP ships local logo and metadata declaring its installed image path',async()=>{
 const root=await fixture();await run('python3',[join(root,'scripts/package-plugin.py')]);const {stdout}=await run('python3',['-c',"import json,zipfile,sys; z=zipfile.ZipFile(sys.argv[1]); d=json.loads(z.read('meta.json')); print(json.dumps({'imagePath':d['imagePath'],'assemblies':d['assemblies'],'version':d['version'],'logo':z.read(d['imagePath']).hex()}))",join(root,'artifacts/WatermelonFin.Upload-0.1.3.0.zip')]);const value=JSON.parse(stdout);assert.equal(value.imagePath,'logo.png');assert.deepEqual(value.assemblies,['WatermelonFin.Plugin.dll']);assert.equal(value.version,'0.1.3.0');assert.equal(value.logo,(await readFile(resolve('assets/logo.png'))).toString('hex'));
});
