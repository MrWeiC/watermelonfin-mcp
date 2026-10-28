import test from 'node:test';
import assert from 'node:assert/strict';
import {Jellyfin, discover, validateHost} from '../src/jellyfin.js';

async function fixture(fn) {
 const calls=[]; const original=globalThis.fetch;
 globalThis.fetch=async (url,options)=>{
  assert.equal(options.redirect,'manual');
  const req={url:new URL(url).pathname,method:options.method,headers:{'authorization':options.headers.Authorization}};
  calls.push({url:req.url,method:req.method,token:req.headers.authorization});
  let status=200,headers={},body='';
  const res={set statusCode(v){status=v;},writeHead(v,h){status=v;headers=h;},end(v=''){body=v;}};
  fn(req,res);return new Response(body,{status,headers});
 };
 return {url:'http://127.0.0.1:8096',calls,close:async()=>{globalThis.fetch=original;}};
}
const info={Id:'server-1',ServerName:'Home',Version:'10.11.0',ProductName:'Jellyfin'};
test('host input rejects paths, credentials and argument injection',()=>{
 for(const h of ['http://host','host/path','user@host','--help','host?x=1','host:80','']) assert.throws(()=>validateHost(h));
 for(const h of ['127.0.0.1','100.64.1.2','media.tail123.ts.net','::1']) assert.doesNotThrow(()=>validateHost(h));
});
test('discovery verifies Jellyfin and uses specified port',async()=>{
 const f=await fixture((q,s)=>s.end(JSON.stringify(info)));
 try { const r=await discover('127.0.0.1',[Number(new URL(f.url).port)]);assert.equal(r.servers[0].serverId,'server-1');assert.equal(f.calls[0].token,undefined); }finally{await f.close();}
});
test('discovery bounds port attempts and rejects duplicates/invalid values',async()=>{
 await assert.rejects(()=>discover('127.0.0.1',Array(17).fill(1)));
 await assert.rejects(()=>discover('127.0.0.1',[0]));
});
test('redirects rejected without following or exposing token',async()=>{
 const f=await fixture((q,s)=>{s.writeHead(302,{Location:'http://example.com'});s.end();});
 try{await assert.rejects(()=>new Jellyfin(f.url).publicInfo(),/redirect/i);assert.equal(f.calls.length,1);}finally{await f.close();}
});
test('invalid public response is not a discovered Jellyfin server',async()=>{
 const f=await fixture((q,s)=>s.end(JSON.stringify({Id:'x',Version:'1'})));
 try{assert.equal((await discover('127.0.0.1',[Number(new URL(f.url).port)])).servers.length,0);}finally{await f.close();}
});
test('credentials refused on untrusted HTTP and wrong identity',async()=>{
 const f=await fixture((q,s)=>s.end(JSON.stringify(info)));
 try{
 await assert.rejects(()=>new Jellyfin(f.url,{token:'SECRET',serverId:'server-1'}).libraries(),/transport/i);
 assert.equal(f.calls.length,0);
 await assert.rejects(()=>new Jellyfin(f.url,{token:'SECRET',serverId:'wrong',trustedHttp:true}).libraries(),/identity/i);
 assert.equal(f.calls[0].token,undefined);
 }finally{await f.close();}
});
test('privileged or deletion-enabled user is rejected',async()=>{
 const f=await fixture((q,s)=>s.end(JSON.stringify(q.url==='/System/Info/Public'?info:{Id:'u',Policy:{IsAdministrator:true,EnableContentDeletion:false}})));
 try{await assert.rejects(()=>new Jellyfin(f.url,{token:'SECRET',serverId:'server-1',trustedHttp:true}).libraries(),/privileged/i);assert.equal(f.calls.length,2);}finally{await f.close();}
});
test('library reads are projected and all outbound requests are GET',async()=>{
 const f=await fixture((q,s)=>s.end(JSON.stringify(q.url==='/System/Info/Public'?info:q.url==='/Users/Me'?{Id:'u',Policy:{IsAdministrator:false,EnableContentDeletion:false}}:{Items:[{Id:'lib',Name:'Movies',CollectionType:'movies',Path:'/secret/path',AccessToken:'SECRET'}]})));
 try{
 const result=await new Jellyfin(f.url,{token:'SECRET',serverId:'server-1',trustedHttp:true}).libraries();
 assert.deepEqual(result,[{id:'lib',name:'Movies',type:'movies'}]);
 assert.ok(f.calls.every(c=>c.method==='GET'));assert.equal(f.calls[0].token,undefined);
 }finally{await f.close();}
});
test('server errors do not echo credentials or server body',async()=>{
 const f=await fixture((q,s)=>{s.statusCode=500;s.end('SECRET');});
 try{await assert.rejects(()=>new Jellyfin(f.url).publicInfo(),e=>!e.message.includes('SECRET'));}finally{await f.close();}
});
test('recognizes Jellyfin Server product label returned by actual target',async()=>{
 const f=await fixture((q,s)=>s.end(JSON.stringify({...info,ProductName:'Jellyfin Server'})));
 try{assert.equal((await new Jellyfin(f.url).publicInfo()).serverId,'server-1');}finally{await f.close();}
});
