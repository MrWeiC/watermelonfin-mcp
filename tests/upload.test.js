import test from 'node:test';
import assert from 'node:assert/strict';
import {Readable,Writable} from 'node:stream';
import {mkdtemp,writeFile,readFile,mkdir} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createHash} from 'node:crypto';
import {ImportStore} from '../src/import-store.js';
import {createHandler} from '../src/import-http.js';
import {uploadFile} from '../src/uploader.js';

async function setup(){
 const root=await mkdtemp(join(tmpdir(),'wf-http-'));const store=new ImportStore(join(root,'server'));await store.init();
 const handler=createHandler(store,'a'.repeat(48));
 const request=async(path,{method='GET',body,token='a'.repeat(48)}={})=>{
  const req=Readable.from(body?[Buffer.from(body)]:[]);req.url=path;req.method=method;req.headers={authorization:`Bearer ${token}`};
  let chunks=[];const res=new Writable({write(c,e,cb){chunks.push(c);cb();}});res.statusCode=200;res.setHeader=()=>{};
  await handler(req,res);await new Promise(r=>res.writableFinished?r():res.once('finish',r));return new Response(Buffer.concat(chunks),{status:res.statusCode});
 };return {root,store,request};
}
test('HTTP handler rejects deletes, bad authorization and cross-job grants',async()=>{
 const {request}=await setup();assert.equal((await request('/jobs',{method:'DELETE'})).status,405);assert.equal((await request('/jobs',{token:'bad'})).status,401);
 const data=Buffer.from('abc'),sha256=createHash('sha256').update(data).digest('hex');
 const a=await (await request('/jobs',{method:'POST',body:JSON.stringify({fileName:'A (2024).mp4',size:3,sha256})})).json();
 const b=await (await request('/jobs',{method:'POST',body:JSON.stringify({fileName:'B (2024).mp4',size:3,sha256})})).json();
 assert.equal((await request(`/jobs/${b.id}`,{token:a.grant})).status,401);
 assert.equal((await request(`/jobs/${a.id}?offset=0`,{method:'PATCH',body:data,token:a.grant})).status,200);
 assert.equal((await request(`/jobs/${a.id}/finalize`,{method:'POST',token:a.grant})).status,200);
});
test('uploader streams direct to importer, never returns grants, refuses files outside upload roots',async()=>{
 const {root,request,store}=await setup();const local=join(root,'local');await mkdir(local);const file=join(local,'Example (2024).mp4');await writeFile(file,'abc');
 const original=globalThis.fetch;globalThis.fetch=async(url,opts={})=>{
  const u=new URL(url);return request(u.pathname+u.search,{method:opts.method,body:opts.body,token:opts.headers.Authorization.slice(7)});
 };
 try{
 const options={endpoint:'https://importer.tail.ts.net',token:'a'.repeat(48),roots:[local]};
 await assert.rejects(()=>uploadFile(join(root,'outside.mp4'),options));
 const r=await uploadFile(file,options);assert.equal(r.status,'completed');assert.ok(!JSON.stringify(r).includes('grant'));
 assert.equal(await readFile(join(store.root,r.relativePath),'utf8'),'abc');
 }finally{globalThis.fetch=original;}
});
