import test from 'node:test';
import assert from 'node:assert/strict';
import {Jellyfin} from '../src/jellyfin.js';
const info={Id:'s',ServerName:'Home',Version:'12.4',ProductName:'Jellyfin Server'};
const user={Id:'u',Policy:{IsAdministrator:false,EnableContentDeletion:false}};
function fixture(fn){const original=globalThis.fetch,calls=[];globalThis.fetch=async(url,options)=>{calls.push({path:new URL(url).pathname,...options});return fn(calls.at(-1));};return {calls,close:()=>{globalThis.fetch=original;}};}
test('username/password logs in without prior browser session or token',async()=>{
 const f=fixture(c=>new Response(JSON.stringify(c.path==='/System/Info/Public'?info:c.path==='/Users/AuthenticateByName'?{AccessToken:'PRIVATE_TOKEN',User:user}:c.path==='/Users/Me'?user:{Items:[]})));
 try{
 const client=new Jellyfin('https://media.tail.ts.net',{serverId:'s',username:'person',password:'PRIVATE_PASSWORD'});
 assert.deepEqual(await client.libraries(),[]);assert.deepEqual(await client.libraries(),[]);
 const login=f.calls.filter(c=>c.path==='/Users/AuthenticateByName');assert.equal(login.length,1);assert.equal(login[0].method,'POST');assert.equal(login[0].redirect,'manual');assert.deepEqual(JSON.parse(login[0].body),{Username:'person',Pw:'PRIVATE_PASSWORD'});assert.ok(login[0].headers['X-Emby-Authorization'].includes('WatermelonFin MCP'));
 assert.ok(f.calls.filter(c=>c.path==='/Users/Me').every(c=>c.headers['X-Emby-Token']==='PRIVATE_TOKEN'));
 }finally{f.close();}
});
test('password is never transmitted without transport trust or matching server ID',async()=>{
 const f=fixture(()=>new Response(JSON.stringify(info)));try{
 await assert.rejects(()=>new Jellyfin('http://media',{serverId:'s',username:'u',password:'secret'}).identity(),/transport/);assert.equal(f.calls.length,0);
 await assert.rejects(()=>new Jellyfin('https://media',{serverId:'wrong',username:'u',password:'secret'}).identity(),/identity/);assert.ok(f.calls.every(c=>c.method==='GET'));
 }finally{f.close();}
});
test('login redirect and failed credentials do not echo server body or password',async()=>{
 for(const status of [302,401]){const f=fixture(c=>c.path==='/System/Info/Public'?new Response(JSON.stringify(info)):new Response('PRIVATE_PASSWORD',{status,headers:{Location:'https://evil.example'}}));try{
 await assert.rejects(()=>new Jellyfin('https://media',{serverId:'s',username:'u',password:'PRIVATE_PASSWORD'}).identity(),e=>!e.message.includes('PRIVATE_PASSWORD'));
 assert.equal(f.calls.length,2);
 }finally{f.close();}}
});
test('explicit retry recovers from transient login failure without automatic retries',async()=>{
 let attempts=0;const tokens=[];
 const f=fixture(c=>c.path==='/System/Info/Public'?new Response(JSON.stringify(info)):c.path==='/Users/AuthenticateByName'?(++attempts===1?new Response('failed',{status:503}):new Response(JSON.stringify({AccessToken:'NEW_SESSION_TOKEN'}))):new Response(JSON.stringify(user)));
 try{
 const c=new Jellyfin('https://media',{serverId:'s',username:'u',password:'secret',onToken:t=>tokens.push(t)});
 await assert.rejects(()=>c.identity());assert.equal(attempts,1);
 assert.equal(await c.identity(),'u');assert.equal(attempts,2);assert.deepEqual(tokens,['NEW_SESSION_TOKEN']);
 }finally{f.close();}
});
