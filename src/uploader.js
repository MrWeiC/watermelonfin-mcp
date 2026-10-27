import {open,realpath,lstat} from 'node:fs/promises';
import {constants} from 'node:fs';
import {resolve,relative,isAbsolute,basename,sep,dirname} from 'node:path';
import {createHash} from 'node:crypto';
import {baseURL} from './jellyfin.js';
import {assertPrivateDirectory,assertTrustedAncestors,verifyDirectoryIdentity} from './filesystem-trust.js';
export async function openSelectedFile(path,roots,{openFile=open}={}){
 if(!Array.isArray(roots)||!roots.length)throw new Error('No host-authorized upload roots');
 const file=await realpath(resolve(path));if((await lstat(resolve(path))).isSymbolicLink())throw new Error('Symlink upload refused');
 let root;for(const candidate of roots){const canonical=await realpath(candidate),rel=relative(canonical,file);if(rel!==''&&!rel.startsWith(`..${sep}`)&&rel!=='..'&&!isAbsolute(rel)){root=canonical;break;}}
 if(!root)throw new Error('File is outside host-authorized upload roots');
 await assertTrustedAncestors(root);const snapshot=await assertPrivateDirectory(root);
 let dir=dirname(file);while(dir!==root){await assertPrivateDirectory(dir);dir=dirname(dir);}
 const handle=await openFile(file,constants.O_RDONLY|(constants.O_NOFOLLOW??0));
 try{await verifyDirectoryIdentity(root,snapshot);if(await realpath(file)!==file)throw new Error('Upload containment changed');const current=await lstat(file),opened=await handle.stat();if(!opened.isFile()||current.ino!==opened.ino||current.dev!==opened.dev)throw new Error('Selected file changed');return {handle,file};}catch(e){await handle.close();throw e;}
}
export async function uploadFile(path,{endpoint,token,roots,trustedHttp=false,jobId}){
 const u=baseURL(endpoint);if(u.protocol!=='https:'&&!trustedHttp)throw new Error('needs_transport_trust: importer requires HTTPS or host-configured HTTP trust');
 if(!token||token.length<32)throw new Error('Importer credential unavailable');
 const {handle,file}=await openSelectedFile(path,roots);
 async function call(route,method='GET',bytes,credential=token){
  let r;try{r=await fetch(new URL(route,u),{method,body:bytes,redirect:'manual',signal:AbortSignal.timeout(60000),headers:{Authorization:`Bearer ${credential}`}});}catch{throw new Error('Importer unreachable; retry with returned job ID if available');}
  if(!r.ok){await r.body?.cancel();throw new Error(`Importer HTTP ${r.status}`);}return r.json();
 }
 let job;
 try{
  const st=await handle.stat();if(!st.isFile()||st.size<1||st.size>50*1024**3)throw new Error('Invalid local upload file size');
  const hash=createHash('sha256');for await(const b of handle.createReadStream({autoClose:false}))hash.update(b);const sha256=hash.digest('hex');
  if(jobId){if(!/^[a-f0-9-]{36}$/.test(jobId))throw new Error('Invalid job ID');job=await call(`jobs/${jobId}`);if(job.size!==st.size||job.sha256!==sha256||job.fileName!==basename(file))throw new Error('Resume file does not match upload job');}
  else job=await call('jobs','POST',JSON.stringify({fileName:basename(file),size:st.size,sha256}));
  let offset=job.offset;while(offset<st.size){const bytes=Buffer.alloc(Math.min(4*1024**2,st.size-offset));const {bytesRead}=await handle.read(bytes,0,bytes.length,offset);if(bytesRead!==bytes.length)throw new Error('Local file changed during upload');const status=await call(`jobs/${job.id}?offset=${offset}`,'PATCH',bytes,job.grant);offset=status.offset;}
  const result=await call(`jobs/${job.id}/finalize`,'POST',undefined,job.grant);return {id:result.id,status:result.status,size:result.size,relativePath:result.relativePath,indexedInJellyfin:false};
 }catch(e){if(job?.id)throw new Error(`Upload interrupted; job ${job.id} can be resumed. ${e instanceof Error?e.message:'Operation failed'}`);throw e;}finally{await handle.close();}
}
