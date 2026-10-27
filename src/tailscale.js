import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
const exec=promisify(execFile);
export function parseTailscale(data){
 const project=p=>({hostname:typeof p?.DNSName==='string'?p.DNSName.replace(/\.$/,''):undefined,addresses:Array.isArray(p?.TailscaleIPs)?p.TailscaleIPs.filter(x=>typeof x==='string'):[],online:p?.Online===true});
 return {status:data?.BackendState??'unknown',self:project(data?.Self),peers:Object.values(data?.Peer??{}).map(project),note:'Peer names do not prove which peer runs Jellyfin. Verify Jellyfin identity and transport trust separately.'};
}
export async function inspectTailscale(){
 for(const path of ['/Applications/Tailscale.app/Contents/MacOS/Tailscale','/usr/local/bin/tailscale','/opt/homebrew/bin/tailscale','/usr/bin/tailscale']){
  try{const {stdout}=await exec(path,['status','--json'],{timeout:5000,maxBuffer:1024*1024,env:{PATH:'/usr/bin:/bin'}});return parseTailscale(JSON.parse(stdout));}catch{}
 }
 return {status:'unavailable',hint:'Local Tailscale CLI unavailable or disconnected; this does not describe the remote server.'};
}
