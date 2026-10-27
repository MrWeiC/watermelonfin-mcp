import {basename} from 'node:path';
import {createHash} from 'node:crypto';
import {openSelectedFile} from './uploader.js';
import {safeName} from './import-plan.js';

export async function inspectUploadPlugin(jellyfin){
 const caps=await jellyfin.pluginRequest('Capabilities');
 if(caps?.protocol!=='watermelonfin-upload.v1'||caps.delete!==false||caps.overwrite!==false)throw new Error('Unsupported upload plugin protocol or unsafe capabilities');
 return caps;
}
export async function uploadToPlugin(path,{jellyfin,roots,targetId,fileName,jobId}){
 if(!/^[a-z0-9][a-z0-9-]{0,40}$/.test(targetId))throw new Error('Invalid upload target');
 await inspectUploadPlugin(jellyfin);
 const targets=await jellyfin.pluginRequest('Targets');
 if(!Array.isArray(targets)||!targets.some(t=>t.id===targetId))throw new Error('Upload target is not authorized');
 const {handle,file}=await openSelectedFile(path,roots);let job;
 try{
  const name=safeName(fileName??basename(file));const stat=await handle.stat();
  if(stat.size<1||stat.size>50*1024**3)throw new Error('Invalid upload file size');
  const hash=createHash('sha256');for await(const chunk of handle.createReadStream({autoClose:false}))hash.update(chunk);const sha256=hash.digest('hex');
  if(jobId){job=await jellyfin.pluginRequest(`Jobs/${jobId}`);if(job.size!==stat.size||job.fileName!==name||job.sha256!==sha256||job.targetId!==targetId)throw new Error('Selected file does not match resume job');}
  else job=await jellyfin.pluginRequest('Jobs','POST',JSON.stringify({targetId,fileName:name,size:stat.size,sha256}));
  if(!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/.test(job?.id)||!Number.isSafeInteger(job.offset)||job.offset<0||job.offset>stat.size)throw new Error('Invalid upload job response');
  let offset=job.offset;
  while(offset<stat.size){
   const bytes=Buffer.alloc(Math.min(4*1024**2,stat.size-offset));const {bytesRead}=await handle.read(bytes,0,bytes.length,offset);if(bytesRead!==bytes.length)throw new Error('Selected file changed during upload');
   const progress=await jellyfin.pluginRequest(`Jobs/${job.id}?offset=${offset}`,'PATCH',bytes);
   if(progress.offset!==offset+bytes.length)throw new Error('Plugin returned an invalid upload offset');offset=progress.offset;
  }
  const result=await jellyfin.pluginRequest(`Jobs/${job.id}/Finalize`,'POST');
  if(result.id!==job.id||result.status!=='completed')throw new Error('Plugin did not confirm publication');
  return {id:result.id,status:result.status,size:result.size,relativePath:result.relativePath,indexedInJellyfin:false};
 }catch(e){if(job?.id)throw new Error(`Upload job ${job.id} interrupted; retry with jobId. ${e instanceof Error?e.message:'Upload failed'}`);throw e;}finally{await handle.close();}
}
