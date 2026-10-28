import test from 'node:test';
import assert from 'node:assert/strict';
import {Client} from '@modelcontextprotocol/sdk/client/index.js';
import {StdioClientTransport} from '@modelcontextprotocol/sdk/client/stdio.js';
import {resolve} from 'node:path';
import {parseTailscale} from '../src/tailscale.js';

async function withClient(fn){
 const transport=new StdioClientTransport({command:process.execPath,args:[resolve('src/cli.js')],env:{PATH:process.env.PATH},stderr:'pipe'});
 const client=new Client({name:'test-agent',version:'1.0.0'});
 try{await client.connect(transport);await fn(client);}finally{await client.close();}
}
test('official MCP client initializes over stdio and gets recipe',async()=>withClient(async c=>{
 const tools=(await c.listTools()).tools;
 assert.ok(tools.some(t=>t.name==='discover_server'));
 for(const t of tools)assert.ok(!/delete|remove|shell|http_request|install_plugin/.test(t.name));
 const prompts=await c.listPrompts();assert.ok(prompts.prompts.some(p=>p.name==='onboard_jellyfin'));
 const recipe=await c.getPrompt({name:'onboard_jellyfin',arguments:{host:'100.64.0.1'}});
 assert.match(recipe.messages[0].content.text,/100\.64\.0\.1/);
 const caps=await c.callTool({name:'inspect_capabilities',arguments:{}});
 assert.equal(caps.isError,undefined);assert.match(caps.content[0].text,/false/);
}));
test('unknown tools and unknown extra arguments are rejected',async()=>withClient(async c=>{
 for(const [name,args] of [['delete_library',{id:'all'}],['discover_server',{host:'127.0.0.1',command:'rm -rf /'}]]){
 const r=await c.callTool({name,arguments:args});assert.equal(r.isError,true);
 }
}));
test('missing credentials are explicit, not silently elevated',async()=>withClient(async c=>{
 const r=await c.callTool({name:'list_libraries',arguments:{profileId:'default'}});assert.equal(r.isError,true);assert.match(r.content[0].text,/profile|auth/);
}));
test('tailscale projections omit keys and host metadata',()=>{
 const peers=parseTailscale({Self:{DNSName:'agent.tail.ts.net',TailscaleIPs:['100.64.0.1']},Peer:{p:{DNSName:'media.tail.ts.net.',TailscaleIPs:['100.64.0.2'],Online:true,PublicKey:'SECRET'}}});
 assert.equal(peers.peers[0].hostname,'media.tail.ts.net');assert.ok(!JSON.stringify(peers).includes('SECRET'));
});
test('planner tool only returns new-file proposals and rejects traversal',async()=>withClient(async c=>{
 const r=await c.callTool({name:'plan_import',arguments:{fileName:'Example S01E01.mkv'}});assert.match(r.content[0].text,/Season 01/);
 const bad=await c.callTool({name:'plan_import',arguments:{fileName:'..\/existing.mkv'}});assert.equal(bad.isError,true);
}));
test('capability response refuses credential-bearing profile URLs',async()=>{
 const transport=new StdioClientTransport({command:process.execPath,args:[resolve('src/cli.js')],env:{PATH:process.env.PATH,WATERMELONFIN_PROFILES:JSON.stringify([{id:'bad',url:'https://user:do-not-echo@host',serverId:'s',tokenEnv:'TOKEN'}])},stderr:'pipe'});
 const client=new Client({name:'test',version:'1'});try{await client.connect(transport);const r=await client.callTool({name:'inspect_capabilities',arguments:{}});assert.equal(r.isError,true);assert.ok(!r.content[0].text.includes('do-not-echo'));}finally{await client.close();}
});

test('MCP initialization advertises the bundled WatermelonFin logo without a network dependency',async()=>withClient(async c=>{
 const {readFile}=await import('node:fs/promises');const info=c.getServerVersion();assert.equal(info.title,'WatermelonFin');assert.equal(info.icons?.[0]?.mimeType,'image/png');assert.ok(info.icons[0].src.startsWith('data:image/png;base64,'));const bytes=Buffer.from(info.icons[0].src.split(',')[1],'base64');assert.deepEqual(bytes,await readFile('assets/logo.png'));
}));
