import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,readFile,writeFile,readdir} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createHash} from 'node:crypto';
import {planImport} from '../src/import-plan.js';
import {ImportStore} from '../src/import-store.js';
const digest=b=>createHash('sha256').update(b).digest('hex');
test('planner refuses paths and ambiguous names',()=>{
 for(const name of ['../movie.mp4','a/b.mp4','a\\b.mp4','CON.mp4','x.txt'])assert.throws(()=>planImport(name));
 assert.equal(planImport('video.mp4').status,'needs_clarification');
 assert.equal(planImport('Example (2024).mkv').relativePath,'Movies/Example (2024)/Example (2024).mkv');
 assert.equal(planImport('Example S01E03.mkv').relativePath,'TV/Example/Season 01/Example - S01E03.mkv');
});
test('new upload finalizes without overwriting, and retries are idempotent',async()=>{
 const root=await mkdtemp(join(tmpdir(),'wf-import-'));const s=new ImportStore(root);await s.init();
 await writeFile(join(root,'existing.mkv'),'existing');
 const b=Buffer.from('new media');const job=await s.create({fileName:'Example (2024).mkv',size:b.length,sha256:digest(b)});
 await s.append(job.id,0,b.subarray(0,4));await s.append(job.id,4,b.subarray(4));
 const result=await s.finalize(job.id);assert.equal(result.status,'completed');
 assert.equal((await s.finalize(job.id)).relativePath,result.relativePath);
 assert.equal(await readFile(join(root,result.relativePath),'utf8'),'new media');
 assert.equal(await readFile(join(root,'existing.mkv'),'utf8'),'existing');
 const restored=new ImportStore(root);await restored.init();assert.equal((await restored.status(job.id)).status,'completed');
});
test('reject wrong offset, excess bytes, wrong hash, unknown job and traversal',async()=>{
 const root=await mkdtemp(join(tmpdir(),'wf-import-'));const s=new ImportStore(root);await s.init();
 const job=await s.create({fileName:'Example (2024).mp4',size:3,sha256:digest(Buffer.from('abc'))});
 await assert.rejects(()=>s.append(job.id,1,Buffer.from('x')));
 await assert.rejects(()=>s.append(job.id,0,Buffer.from('abcd')));
 await assert.rejects(()=>s.status('../existing'));
 await s.append(job.id,0,Buffer.from('xyz'));await assert.rejects(()=>s.finalize(job.id),/checksum/);
 assert.deepEqual(await readdir(join(root,'Imports')),[]);
});
test('same named imports get distinct destinations; finalized bytes immutable via API',async()=>{
 const root=await mkdtemp(join(tmpdir(),'wf-import-'));const s=new ImportStore(root);await s.init();
 const outputs=[];for(let i=0;i<2;i++){
 const job=await s.create({fileName:'Example (2024).mp4',size:1,sha256:digest(Buffer.from('x'))});await s.append(job.id,0,Buffer.from('x'));outputs.push(await s.finalize(job.id));await assert.rejects(()=>s.append(job.id,1,Buffer.from('y')));
 }assert.notEqual(outputs[0].relativePath,outputs[1].relativePath);
});
