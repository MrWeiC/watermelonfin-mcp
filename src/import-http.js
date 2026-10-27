import {createHmac,timingSafeEqual} from 'node:crypto';
import {z} from 'zod';
const equal=(a,b)=>typeof a==='string'&&Buffer.byteLength(a)===Buffer.byteLength(b)&&timingSafeEqual(Buffer.from(a),Buffer.from(b));
const createSchema=z.object({fileName:z.string().max(180),size:z.number().int().positive(),sha256:z.string().regex(/^[a-f0-9]{64}$/)}).strict();
async function body(req,max){const chunks=[];let size=0;for await(const chunk of req){size+=chunk.length;if(size>max)throw new Error('Request size limit exceeded');chunks.push(chunk);}return Buffer.concat(chunks);}
export function createHandler(store,secret){
 if(typeof secret!=='string'||secret.length<32)throw new Error('Importer requires a strong host-configured secret');
 const sign=(id,expiry)=>`${id}.${expiry}.${createHmac('sha256',secret).update(`${id}.${expiry}`).digest('hex')}`;
 const valid=(token,id)=>{const fields=token.split('.');return fields.length===3&&fields[0]===id&&Number(fields[1])>Date.now()&&equal(token,sign(id,fields[1]));};
 return async(req,res)=>{
  const send=(status,data)=>{res.statusCode=status;res.setHeader('Content-Type','application/json');res.setHeader('Cache-Control','no-store');res.end(JSON.stringify(data));};
  try{
   if(!['GET','POST','PATCH'].includes(req.method))return send(405,{error:'Operation not available'});
   const token=req.headers.authorization?.replace(/^Bearer /,'')??'';const master=equal(token,secret);
   const url=new URL(req.url,'http://localhost');const match=/^\/jobs\/([a-f0-9-]{36})(\/finalize)?$/.exec(url.pathname);
   if(!master&&(!match||!valid(token,match[1])))return send(401,{error:'Unauthorized'});
   if(url.pathname==='/jobs'&&req.method==='POST'){
    const input=createSchema.parse(JSON.parse((await body(req,8192)).toString('utf8')));
    const job=await store.exclusive('create',()=>store.create(input));return send(201,{...job,grant:sign(job.id,job.expiresAt)});
   }
   if(!match)return send(404,{error:'Unknown endpoint'});
   const id=match[1];
   if(req.method==='GET'&&!match[2]){const status=await store.status(id);return send(200,master?{...status,grant:sign(id,(await store.load(id)).expiresAt),fileName:(await store.load(id)).fileName,sha256:(await store.load(id)).sha256}:status);}
   if(req.method==='PATCH'&&!match[2]){
    const offset=Number(url.searchParams.get('offset'));if(!url.searchParams.has('offset')||!Number.isSafeInteger(offset))throw new Error('Invalid offset');
    return send(200,await store.append(id,offset,await body(req,4*1024**2)));
   }
   if(req.method==='POST'&&match[2])return send(200,await store.finalize(id));
   return send(405,{error:'Operation not available'});
  }catch(e){const message=e instanceof Error?e.message:'';const known=/^(Invalid|Wrong offset|Job |Completed|Upload |needs_clarification|Unsupported|Request size|Insufficient|Job quota)/.test(message);return send(400,{error:known?message:'Importer request failed'});}
 };
}
