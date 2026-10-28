import {isIP} from 'node:net';
import {randomUUID} from 'node:crypto';
const deviceId=randomUUID();

export function validateHost(host) {
 if(typeof host!=='string'||host.length>253||(!isIP(host)&&! /^(?=.{1,253}$)[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?$/i.test(host))) throw new Error('Invalid host: supply one IP address or DNS hostname, without port or path');
 return host;
}
export function baseURL(value) {
 const u=new URL(value);
 if(!['http:','https:'].includes(u.protocol)||u.username||u.password||u.search||u.hash) throw new Error('Invalid server URL');
 validateHost(u.hostname.replace(/^\[|\]$/g,''));
 u.pathname=u.pathname.replace(/\/$/,'')+'/';
 return u;
}
function authorization(token) {
 const fields = {Client:'WatermelonFin MCP',Device:'Local Agent',DeviceId:deviceId,Version:'0.1.2',...(token?{Token:token}:{})};
 return 'MediaBrowser '+Object.entries(fields).map(([key,value])=>`${key}=${JSON.stringify(value)}`).join(', ');
}
async function readJSON(url,token,login) {
 let r;
 try{r=await fetch(url,{method:'GET',redirect:'manual',signal:AbortSignal.timeout(3500),headers:{...(login?{'Content-Type':'application/json'}:{}),...((login||token)?{Authorization:authorization(token)}:{})} ,...(login?{method:'POST',body:JSON.stringify(login)}:{})});}catch{throw new Error('Server unreachable or TLS verification failed');}
 return parseResponse(r);
}
async function parseResponse(r){
 if(r.status>=300&&r.status<400) {await r.body?.cancel();throw new Error('Server redirect refused');}
 if(!r.ok) {await r.body?.cancel();throw new Error(`Jellyfin HTTP ${r.status}`);}
 const reader=r.body?.getReader(); if(!reader) throw new Error('Empty server response');
 let size=0;const chunks=[];
 try{while(true){const {done,value}=await reader.read();if(done)break;size+=value.length;if(size>1024*1024){await reader.cancel();throw new Error('Server response exceeds limit');}chunks.push(value);}}catch{throw new Error('Server response interrupted or exceeds limit');}
 try{return JSON.parse(Buffer.concat(chunks).toString('utf8'));}catch{throw new Error('Invalid server JSON');}
}
export class Jellyfin {
 constructor(url,options={}){this.url=baseURL(url);this.options=options;}
 async publicInfo(){
  const v=await readJSON(new URL('System/Info/Public',this.url));
  if(!v||typeof v.Id!=='string'||!v.Id||typeof v.ServerName!=='string'||typeof v.Version!=='string'||!['Jellyfin','Jellyfin Server'].includes(v.ProductName))throw new Error('Not a verified Jellyfin response');
  return {serverId:v.Id,name:v.ServerName,version:v.Version,url:this.url.href.replace(/\/$/,'')};
 }
 async identity(){
  if(!this.options.token&&!(typeof this.options.username==='string'&&this.options.username&&typeof this.options.password==='string'&&this.options.password))throw new Error('needs_auth: provide local username/password credentials or a dedicated Jellyfin user token');
  if(this.url.protocol!=='https:'&&!this.options.trustedHttp)throw new Error('needs_transport_trust: HTTP requires explicit local trust configuration');
  const info=await this.publicInfo();
  if(!this.options.serverId||info.serverId!==this.options.serverId)throw new Error('Server identity mismatch');
  if(!this.options.token){
   this.loginTask??=readJSON(new URL('Users/AuthenticateByName',this.url),undefined,{Username:this.options.username,Pw:this.options.password}).then(result=>{if(typeof result?.AccessToken!=='string'||!result.AccessToken)throw new Error('Invalid authentication response');this.options.token=result.AccessToken;this.options.onToken?.(result.AccessToken);}).catch(error=>{this.loginTask=undefined;throw error;});
   await this.loginTask;
  }
  const me=await readJSON(new URL('Users/Me',this.url),this.options.token);
  const p=me?.Policy;
  if(!me?.Id||!p||p.IsAdministrator!==false||p.EnableContentDeletion!==false||p.EnableContentDeletionFromFolders?.length)throw new Error('Privileged or deletion-enabled credentials refused; use a dedicated restricted user');
  return me.Id;
 }
 async pluginRequest(route,method='GET',body){
  const uuid='[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}';
  const rules={GET:new RegExp(`^(Capabilities|Targets|Jobs/${uuid})$`),POST:new RegExp(`^(Jobs|Jobs/${uuid}/Finalize)$`),PATCH:new RegExp(`^Jobs/${uuid}\\?offset=[0-9]+$`)};
  if(!rules[method]?.test(route))throw new Error('Native upload operation not allowed');
  await this.identity();
  let response;try{response=await fetch(new URL(`WatermelonFin/Upload/${route}`,this.url),{method,redirect:'manual',signal:AbortSignal.timeout(60000),headers:{Authorization:authorization(this.options.token),...(body!==undefined?{'Content-Type':Buffer.isBuffer(body)?'application/octet-stream':'application/json'}:{})},...(body!==undefined?{body}:{} )});}catch{throw new Error('Jellyfin upload plugin unreachable or TLS verification failed');}
  return parseResponse(response);
 }
 async libraries(){
  const user=await this.identity();
  const data=await readJSON(new URL(`Users/${encodeURIComponent(user)}/Views`,this.url),this.options.token);
  if(!Array.isArray(data?.Items))throw new Error('Invalid library response');
  return data.Items.map(x=>({id:x.Id,name:x.Name,type:x.CollectionType}));
 }
 async search(query,limit=20){
  if(typeof query!=='string'||query.length<1||query.length>200||!Number.isInteger(limit)||limit<1||limit>50)throw new Error('Invalid search');
  const user=await this.identity();const u=new URL('Items',this.url);
  u.search=new URLSearchParams({UserId:user,SearchTerm:query,Limit:String(limit),Recursive:'true',IncludeItemTypes:'Movie,Episode,Series,Audio'}).toString();
  const data=await readJSON(u,this.options.token);
  if(!Array.isArray(data?.Items))throw new Error('Invalid search response');
  return data.Items.map(x=>({id:x.Id,name:x.Name,type:x.Type,year:x.ProductionYear}));
 }
}
export async function discover(host,ports=[8096,8920,80,443]) {
 validateHost(host);
 if(!Array.isArray(ports)||ports.length<1||ports.length>16||new Set(ports).size!==ports.length||ports.some(p=>!Number.isInteger(p)||p<1||p>65535))throw new Error('Use 1–16 unique ports between 1 and 65535');
 const authority=isIP(host)===6?`[${host}]`:host;
 const attempts=ports.flatMap(port=>[port===443||port===8920?'https':'http',port===443||port===8920?'http':'https'].map(scheme=>`${scheme}://${authority}:${port}`));
 const servers=[];
 // Bounded concurrency, fixed host. No credential sent during discovery.
 for(let i=0;i<attempts.length;i+=4){await Promise.all(attempts.slice(i,i+4).map(async url=>{try{servers.push(await new Jellyfin(url).publicInfo());}catch{}}));}
 return {status:servers.length?'found':'not_found',servers:servers.sort((a,b)=>a.url.localeCompare(b.url)),hint:servers.length?undefined:'A reverse-proxy hostname, base path, or other port may be needed; discovery does not authenticate this server.'};
}
