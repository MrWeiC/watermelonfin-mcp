import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,writeFile,rename,symlink,open,chmod} from 'node:fs/promises';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {createHash} from 'node:crypto';
import {openSelectedFile} from '../src/uploader.js';
import {ImportStore} from '../src/import-store.js';
const sha=b=>createHash('sha256').update(b).digest('hex');
test('opened upload descriptor rejects ancestor replacement before open',async()=>{
 const root=await mkdtemp(join(tmpdir(),'wf-race-')),allowed=join(root,'allowed'),sub=join(allowed,'sub'),outside=join(root,'outside');
 await mkdir(sub,{recursive:true});await mkdir(outside);await writeFile(join(sub,'X (2024).mp4'),'safe');await writeFile(join(outside,'X (2024).mp4'),'secret');
 await assert.rejects(()=>openSelectedFile(join(sub,'X (2024).mp4'),[allowed],{openFile:async(...args)=>{await rename(sub,join(allowed,'old'));await symlink(outside,sub);return open(...args);}}),/changed|containment|symlink/i);
});
test('importer rejects shared-writable roots',async()=>{
 const root=await mkdtemp(join(tmpdir(),'wf-root-'));await chmod(root,0o777);await assert.rejects(()=>new ImportStore(root).init(),/private|writable|ownership/i);
});
test('completed jobs release active quota and restart preserves this',async()=>{
 const root=await mkdtemp(join(tmpdir(),'wf-quota-'));let store=new ImportStore(root,{maxJobs:1});await store.init();
 const b=Buffer.from('abc'),data={fileName:'X (2024).mp4',size:3,sha256:sha(b)};
 const a=await store.create(data);await store.append(a.id,0,b);await store.finalize(a.id);
 store=new ImportStore(root,{maxJobs:1});await store.init();await assert.doesNotReject(()=>store.create(data));
});
test('symlinked publication ancestor creates no directories outside importer root',async()=>{
 const root=await mkdtemp(join(tmpdir(),'wf-publish-')),outside=join(root,'outside');await mkdir(outside);
 const store=new ImportStore(join(root,'private'));await store.init();const b=Buffer.from('abc');const job=await store.create({fileName:'X (2024).mp4',size:3,sha256:sha(b)});await store.append(job.id,0,b);
 await symlink(outside,join(store.root,'Imports',job.id));await assert.rejects(()=>store.finalize(job.id));
 const {readdir}=await import('node:fs/promises');assert.deepEqual(await readdir(outside),[]);
});
