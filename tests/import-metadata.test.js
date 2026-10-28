import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,symlink,chmod} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {Client} from '@modelcontextprotocol/sdk/client/index.js';
import {StdioClientTransport} from '@modelcontextprotocol/sdk/client/stdio.js';
import {Jellyfin} from '../src/jellyfin.js';
import {inspectImportMetadata,updateImportMetadata,setImportArtwork,restoreImportMetadata,refreshImportMetadata} from '../src/import-metadata.js';
const job='6d2a1938-54f5-4c6b-bc8e-c6c13c82cb08';
const revision='9a03d351-f61d-4533-9b73-929934c2b4af';
const document={title:'NOAH',overview:'Sight & Sound stage production',studios:['Sight & Sound Theatres'],genres:['Faith','Musical','Family']};
const caps={protocol:'watermelonfin-upload.v1',delete:false,overwrite:false,metadataProtocol:'watermelonfin-metadata.v1',ownedImportMetadata:true,metadataRestore:true,maxArtworkBytes:8388608,artworkRoles:['primary','backdrop']};
const reply=(patch={})=>({jobId:job,revision,revisions:['initial'],persistedMetadata:document,indexedMetadata:document,artwork:{primary:true,backdrop:false},verified:true,refreshStatus:'completed',...patch});
const client=()=>new Jellyfin('https://media.tail.ts.net',{token:'TOKEN_SECRET_TEST',serverId:'server'});
const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGOo/w8AAgABf//RyUkAAAAASUVORK5CYII=','base64');
function fixture(handler){const old=globalThis.fetch,calls=[];globalThis.fetch=async(url,options)=>{const u=new URL(url);const call={path:u.pathname,query:u.search,...options};calls.push(call);if(u.pathname==='/System/Info/Public')return Response.json({Id:'server',ServerName:'Home',Version:'12.4.0',ProductName:'Jellyfin Server'});if(u.pathname==='/Users/Me')return Response.json({Id:'user',Policy:{IsAdministrator:false,EnableContentDeletion:false}});if(u.pathname.endsWith('/Capabilities'))return handler?.(call)??Response.json(caps);return handler?.(call)??Response.json(reply());};return{calls,close:()=>globalThis.fetch=old};}

test('metadata tools address only one owned upload job and pass revision preconditions',async()=>{
 const f=fixture(c=>c.path.endsWith('/Capabilities')?Response.json(caps):Response.json(reply()));
 try{
  await inspectImportMetadata(client(),job);
  const result=await updateImportMetadata(client(),job,'initial',document);
  await restoreImportMetadata(client(),job,revision,'initial');
  await refreshImportMetadata(client(),job,revision);
  const mutations=f.calls.filter(c=>c.method==='POST');assert.equal(mutations.length,3);
  assert.deepEqual(mutations.map(c=>c.path),[`/WatermelonFin/Upload/Jobs/${job}/Metadata`,`/WatermelonFin/Upload/Jobs/${job}/Metadata/Restore`,`/WatermelonFin/Upload/Jobs/${job}/Metadata/Refresh`]);
  assert.equal(JSON.parse(mutations[0].body).expectedRevision,'initial');assert.equal(JSON.parse(mutations[1].body).revision,'initial');assert.equal(result.indexedMetadata.title,'NOAH');
 }finally{f.close();}
});
test('old plugins fail explicitly before any metadata mutation',async()=>{
 const f=fixture(c=>Response.json(c.path.endsWith('/Capabilities')?{protocol:'watermelonfin-upload.v1',delete:false,overwrite:false}:reply()));
 try{await assert.rejects(()=>updateImportMetadata(client(),job,'initial',document),/upgrade|metadata.*support/i);assert.equal(f.calls.filter(c=>c.method==='POST').length,0);}finally{f.close();}
});
test('invalid paths IDs unknown metadata fields and invalid dates are rejected',async()=>{
 const f=fixture();try{
  for(const value of [{...document,serverPath:'/foreign'},{...document,posterUrl:'http://evil'},{...document,productionYear:1500},{...document,premiereDate:'2025-02-30'},{...document,providerIds:{imdb:'http://evil'}}])await assert.rejects(()=>updateImportMetadata(client(),job,'initial',value));
  await assert.rejects(()=>inspectImportMetadata(client(),'../../other'));
  await assert.rejects(()=>restoreImportMetadata(client(),job,revision,'../other'));
  assert.equal(f.calls.filter(c=>c.method==='POST').length,0);
 }finally{f.close();}
});
test('artwork streams bounded bytes from allowed roots without returning local bytes or credentials',async()=>{
 const root=await mkdtemp(join(tmpdir(),'wf-metadata-art-'));const file=join(root,'poster.png');await writeFile(file,png);const f=fixture(c=>Response.json(c.path.endsWith('/Capabilities')?caps:reply()));
 try{
  const result=await setImportArtwork(client(),job,'initial','primary',file,[root]);
  const post=f.calls.find(c=>c.path.includes('/Artwork/'));assert.equal(post.path,`/WatermelonFin/Upload/Jobs/${job}/Artwork/primary`);assert.equal(new URLSearchParams(post.query).get('expectedRevision'),'initial');assert.deepEqual(post.body,png);assert.equal(post.headers['Content-Type'],'application/octet-stream');assert.ok(!JSON.stringify(result).includes('TOKEN_SECRET_TEST'));assert.ok(!JSON.stringify(result).includes(png.toString('base64')));
 }finally{f.close();}
});
test('artwork rejects outside roots symlinks oversized files and non-images before mutation',async()=>{
 const root=await mkdtemp(join(tmpdir(),'wf-metadata-safe-'));const outside=await mkdtemp(join(tmpdir(),'wf-metadata-outside-'));const valid=join(root,'poster.png');await writeFile(valid,png);const external=join(outside,'poster.png');await writeFile(external,png);const link=join(root,'link.png');await symlink(valid,link);const large=join(root,'large.png');await writeFile(large,Buffer.alloc(8388609));const junk=join(root,'junk.png');await writeFile(junk,'not an image');const f=fixture();
 try{
  for(const file of [external,link,large,junk])await assert.rejects(()=>setImportArtwork(client(),job,'initial','primary',file,[root]));
  await assert.rejects(()=>setImportArtwork(client(),job,'initial','../primary',valid,[root]));
  await chmod(root,0o777);await assert.rejects(()=>setImportArtwork(client(),job,'initial','primary',valid,[root]));assert.equal(f.calls.filter(c=>c.method==='POST').length,0);
 }finally{await chmod(root,0o700);f.close();}
});
test('metadata redirects are refused and authentication is not forwarded',async()=>{
 const f=fixture(c=>c.path.endsWith('/Capabilities')?Response.json(caps):new Response('',{status:302,headers:{Location:'https://evil.invalid'}}));
 try{await assert.rejects(()=>inspectImportMetadata(client(),job),/redirect/i);assert.ok(f.calls.every(c=>c.path.startsWith('/')));assert.equal(f.calls.filter(c=>c.path.endsWith('/Metadata')).length,1);}finally{f.close();}
});
test('pending refreshes remain unverified and inconsistent verification is rejected',async()=>{
 let state=reply({verified:false,refreshStatus:'pending',indexedMetadata:{...document,title:'Old title'}});const f=fixture(c=>Response.json(c.path.endsWith('/Capabilities')?caps:state));
 try{assert.equal((await updateImportMetadata(client(),job,'initial',document)).verified,false);state=reply({indexedMetadata:{...document,title:'Wrong title'}});await assert.rejects(()=>inspectImportMetadata(client(),job),/verification|inconsistent/i);}finally{f.close();}
});
test('MCP stdio exposes strict job-scoped metadata tools without accepting secrets or arbitrary URLs',async()=>{
 const c=new Client({name:'metadata-protocol-test',version:'1'});try{
  await c.connect(new StdioClientTransport({command:process.execPath,args:['src/cli.js'],env:{PATH:process.env.PATH},stderr:'pipe'}));const tools=(await c.listTools()).tools;
  for(const name of ['inspect_import_metadata','update_import_metadata','set_import_artwork','restore_import_metadata','refresh_import_metadata'])assert.ok(tools.some(t=>t.name===name));
  const result=await c.callTool({name:'update_import_metadata',arguments:{profileId:'home',jobId:job,expectedRevision:'initial',metadata:document,password:'do-not-accept'}});assert.equal(result.isError,true);
  const result2=await c.callTool({name:'set_import_artwork',arguments:{profileId:'home',jobId:job,expectedRevision:'initial',role:'primary',path:'https://evil.invalid/poster.jpg'}});assert.equal(result2.isError,true);
 }finally{await c.close();}
});

test('real MCP stdio round-trip repairs metadata through bounded authenticated HTTP',async()=>{
 const {createServer}=await import('node:http');
 let updated=false,authorized=false;
 const server=createServer(async(req,res)=>{
  res.setHeader('Content-Type','application/json');let data;
  if(req.url==='/System/Info/Public')data={Id:'server',ServerName:'Home',Version:'12.4.0',ProductName:'Jellyfin Server'};
  else if(req.url==='/Users/AuthenticateByName')data={AccessToken:'TEST_SESSION_TOKEN'};
  else if(req.url==='/Users/Me')data={Id:'user',Policy:{IsAdministrator:false,EnableContentDeletion:false}};
  else if(req.url.endsWith('/Capabilities'))data=caps;
  else if(req.url===`/WatermelonFin/Upload/Jobs/${job}/Metadata`&&req.method==='POST'){
   const chunks=[];for await(const b of req)chunks.push(b);const request=JSON.parse(Buffer.concat(chunks));
   updated=request.metadata.title==='NOAH'&&request.expectedRevision==='initial';authorized=req.headers.authorization?.includes('Token="TEST_SESSION_TOKEN"')===true;data=reply();
  }else{res.statusCode=404;data={error:'Unexpected route'};}
  res.end(JSON.stringify(data));
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const origin=`http://127.0.0.1:${server.address().port}`;
 const c=new Client({name:'metadata-integration',version:'1'});
 try{
  await c.connect(new StdioClientTransport({command:process.execPath,args:['src/cli.js'],env:{PATH:process.env.PATH,WATERMELONFIN_TRUSTED_HTTP_ORIGINS:origin,WATERMELONFIN_PROFILES:JSON.stringify([{id:'test',url:origin,serverId:'server',username:'restricted',passwordEnv:'TEST_PASSWORD'}]),TEST_PASSWORD:'LOCAL_FAKE_PASSWORD'},stderr:'pipe'}));
  const r=await c.callTool({name:'update_import_metadata',arguments:{profileId:'test',jobId:job,expectedRevision:'initial',metadata:document}});
  assert.equal(r.isError,undefined);assert.equal(JSON.parse(r.content[0].text).verified,true);assert.ok(updated&&authorized);assert.ok(!JSON.stringify(r).includes('TEST_SESSION_TOKEN'));assert.ok(!JSON.stringify(r).includes('LOCAL_FAKE_PASSWORD'));
 }finally{await c.close();server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}
});
