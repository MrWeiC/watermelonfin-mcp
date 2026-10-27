import {mkdir,lstat,realpath,open,readdir,link,statfs} from 'node:fs/promises';
import {constants} from 'node:fs';
import {join,dirname,resolve,relative,isAbsolute,sep} from 'node:path';
import {randomUUID,createHash,timingSafeEqual} from 'node:crypto';
import {planImport} from './import-plan.js';
import {assertPrivateDirectory,assertTrustedAncestors,verifyDirectoryIdentity} from './filesystem-trust.js';
const idPattern=/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const nofollow=constants.O_NOFOLLOW??0;
export class ImportStore {
 constructor(root,{maxBytes=50*1024**3,maxJobs=100,expiryMs=24*3600*1000}={}){this.root=resolve(root);this.maxBytes=maxBytes;this.maxJobs=maxJobs;this.expiryMs=expiryMs;this.busy=new Set();}
 async directory(path){
 const rel=relative(this.root,resolve(path));if(rel==='..'||rel.startsWith(`..${sep}`)||isAbsolute(rel))throw new Error('Invalid directory containment');
 await verifyDirectoryIdentity(this.root,this.rootIdentity);let current=this.root;
 for(const part of rel.split(sep).filter(Boolean)){current=join(current,part);try{await mkdir(current,{mode:0o700});}catch(e){if(e.code!=='EEXIST')throw e;}await assertPrivateDirectory(current);if(await realpath(current)!==current)throw new Error('Symlinked importer directory refused');}
 }
 async init(){await mkdir(this.root,{recursive:true,mode:0o700});if((await lstat(this.root)).isSymbolicLink())throw new Error('Symlinked importer root refused');this.root=await realpath(this.root);await assertTrustedAncestors(this.root);this.rootIdentity=await assertPrivateDirectory(this.root);await this.directory(join(this.root,'.staging'));await this.directory(join(this.root,'Imports'));}
 paths(id){if(!idPattern.test(id))throw new Error('Invalid job ID');return {dir:join(this.root,'.staging',id),body:join(this.root,'.staging',id,'body'),manifest:join(this.root,'.staging',id,'job.json')};}
 async exclusive(id,fn){if(this.busy.has(id))throw new Error('Job busy; retry');this.busy.add(id);try{return await fn();}finally{this.busy.delete(id);}}
 async create({fileName,size,sha256}){
 await verifyDirectoryIdentity(this.root,this.rootIdentity);
 const plan=planImport(fileName);if(plan.status!=='ready')throw new Error('needs_clarification: filename must identify title/year or episode');
 if(!Number.isSafeInteger(size)||size<1||size>this.maxBytes||! /^[a-f0-9]{64}$/.test(sha256))throw new Error('Invalid upload size or checksum');
 let active=0,reserved=0;for(const id of await readdir(join(this.root,'.staging'))){if(!idPattern.test(id))throw new Error('Invalid staging entry');let m;try{m=await this.load(id);}catch(e){if(e.code==='ENOENT')continue;throw e;}if(Date.now()<=m.expiresAt&&!await this.completed(m)){active++;const st=await lstat(this.paths(id).body);reserved+=Math.max(0,m.size-st.size);}}
 if(active>=this.maxJobs)throw new Error('Job quota exceeded');
 const disk=await statfs(this.root);if(Number(disk.bavail)*Number(disk.bsize)<size+reserved+64*1024**2)throw new Error('Insufficient free disk space');
 const id=randomUUID(),p=this.paths(id);await mkdir(p.dir,{mode:0o700});
 const manifest={id,fileName,size,sha256,plan,expiresAt:Date.now()+this.expiryMs};
 const mf=await open(p.manifest,constants.O_WRONLY|constants.O_CREAT|constants.O_EXCL|nofollow,0o600);try{await mf.writeFile(JSON.stringify(manifest));await mf.sync();}finally{await mf.close();}
 const body=await open(p.body,constants.O_WRONLY|constants.O_CREAT|constants.O_EXCL|nofollow,0o600);await body.close();return {id,size,offset:0,expiresAt:manifest.expiresAt,plan};
 }
 async load(id){await verifyDirectoryIdentity(this.root,this.rootIdentity);const p=this.paths(id);if((await lstat(p.dir)).isSymbolicLink()||await realpath(p.dir)!==p.dir)throw new Error('Invalid job directory');const f=await open(p.manifest,constants.O_RDONLY|nofollow);try{const m=JSON.parse(await f.readFile('utf8'));if(m.id!==id)throw new Error('Invalid manifest');return m;}finally{await f.close();}}
 destination(m){return join(this.root,'Imports',m.id,m.plan.relativePath);}
 async completed(m){try{const a=await lstat(this.destination(m)),b=await lstat(this.paths(m.id).body);return a.isFile()&&!a.isSymbolicLink()&&a.ino===b.ino&&a.dev===b.dev;}catch{return false;}}
 async status(id){const m=await this.load(id);const body=await lstat(this.paths(id).body);if(!body.isFile()||body.isSymbolicLink())throw new Error('Invalid staging file');return {id,size:m.size,offset:body.size,status:await this.completed(m)?'completed':Date.now()>m.expiresAt?'expired':'uploading',relativePath:await this.completed(m)?`Imports/${id}/${m.plan.relativePath}`:undefined};}
 async append(id,offset,bytes){return this.exclusive(id,async()=>{
 const m=await this.load(id);if(await this.completed(m))throw new Error('Completed upload is immutable');if(Date.now()>m.expiresAt)throw new Error('Job expired');
 if(!Buffer.isBuffer(bytes)||bytes.length<1||bytes.length>4*1024**2||!Number.isSafeInteger(offset)||offset<0)throw new Error('Invalid upload chunk');
 const f=await open(this.paths(id).body,constants.O_WRONLY|nofollow);try{const st=await f.stat();if(!st.isFile()||st.size!==offset||offset+bytes.length>m.size)throw new Error('Wrong offset or size limit');let n=0;while(n<bytes.length){const r=await f.write(bytes,n,bytes.length-n,offset+n);n+=r.bytesWritten;}await f.sync();}finally{await f.close();}
 return this.status(id);
 });}
 async finalize(id){return this.exclusive(id,async()=>{
 const m=await this.load(id);if(await this.completed(m))return this.status(id);if(Date.now()>m.expiresAt)throw new Error('Job expired');
 const source=await open(this.paths(id).body,constants.O_RDONLY|nofollow);try{
 if((await source.stat()).size!==m.size)throw new Error('Upload incomplete');const hash=createHash('sha256');for await(const chunk of source.createReadStream({autoClose:false}))hash.update(chunk);
 if(!timingSafeEqual(Buffer.from(m.sha256,'hex'),hash.digest()))throw new Error('Upload checksum mismatch');
 }finally{await source.close();}
 // Publish only inside this newly generated job namespace; hard link is atomic and fails if a destination exists.
 const dest=this.destination(m);await this.directory(dirname(dest));await link(this.paths(id).body,dest);
 return this.status(id);
 });}
}
