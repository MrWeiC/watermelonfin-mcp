import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {Jellyfin} from '../src/jellyfin.js';
import {uploadToPlugin} from '../src/plugin-uploader.js';
const info={Id:'server',ServerName:'Home',Version:'12.4.0',ProductName:'Jellyfin Server'};
const user={Id:'u',Policy:{IsAdministrator:false,EnableContentDeletion:false}};
function fixture(handler){const original=globalThis.fetch,calls=[];globalThis.fetch=async(url,opts)=>{const u=new URL(url);calls.push({path:u.pathname,query:u.search,...opts});if(u.pathname==='/System/Info/Public')return new Response(JSON.stringify(info));if(u.pathname==='/Users/Me')return new Response(JSON.stringify(user));return handler(calls.at(-1));};return {calls,close:()=>{globalThis.fetch=original;}};}
const client=()=>new Jellyfin('https://jellyfin.tail.ts.net',{token:'TOKEN_SECRET',serverId:'server'});
test('plugin uses existing Jellyfin session and bounded native routes',async()=>{
 const f=fixture(c=>new Response(JSON.stringify(c.path.endsWith('Capabilities')?{protocol:'watermelonfin-upload.v1',delete:false,overwrite:false}:{id:'movies'})));
 try{await client().pluginRequest('Capabilities');const c=f.calls.at(-1);assert.equal(c.headers['X-Emby-Token'],'TOKEN_SECRET');assert.equal(c.redirect,'manual');await assert.rejects(()=>client().pluginRequest('http://evil.example','POST'));await assert.rejects(()=>client().pluginRequest('../Library/VirtualFolders','DELETE'));}finally{f.close();}
});
test('selected file streams direct to native plugin without returning credentials',async()=>{
 const root=await mkdtemp(join(tmpdir(),'wf-plugin-client-')),file=join(root,'Example (2024).mp4');await writeFile(file,'abc');let create;
 const id='9a03d351-f61d-4533-9b73-929934c2b4af';
 const f=fixture(c=>{let result;if(c.path.endsWith('Capabilities'))result={protocol:'watermelonfin-upload.v1',delete:false,overwrite:false};else if(c.path.endsWith('Targets'))result=[{id:'movies',kind:'movies'}];else if(c.path.endsWith('/Jobs')){create=JSON.parse(c.body);result={id,offset:0,size:3};}else if(c.path.endsWith('/Finalize'))result={id,status:'completed',size:3,relativePath:'Imported/movies/job/file.mp4'};else result={id,offset:3,size:3};return new Response(JSON.stringify(result));});
 try{const result=await uploadToPlugin(file,{jellyfin:client(),roots:[root],targetId:'movies'});assert.equal(result.status,'completed');assert.equal(create.targetId,'movies');assert.ok(!JSON.stringify(result).includes('TOKEN_SECRET'));assert.ok(f.calls.some(c=>c.method==='PATCH'&&Buffer.isBuffer(c.body)));assert.ok(!f.calls.some(c=>c.method==='DELETE'));}finally{f.close();}
});
test('native plugin redirects never receive a forwarded token',async()=>{
 const f=fixture(()=>new Response('',{status:302,headers:{Location:'https://evil.example'}}));try{await assert.rejects(()=>client().pluginRequest('Capabilities'),/redirect/i);assert.equal(f.calls.length,3);}finally{f.close();}
});
