import {isAbsolute} from 'node:path';
import {z} from 'zod';
import {inspectUploadPlugin} from './plugin-uploader.js';
import {openSelectedFile} from './uploader.js';

const uuid='[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}';
export const importJobId=z.string().regex(new RegExp(`^${uuid}$`));
export const metadataRevision=z.string().regex(new RegExp(`^(initial|${uuid})$`));
export const artworkRole=z.enum(['primary','backdrop']);
export const artworkPath=z.string().min(1).max(4096).refine(isAbsolute,'Artwork requires an absolute local file path');
const date=z.string().regex(/^\d{4}-\d{2}-\d{2}$/).refine(v=>{const d=new Date(`${v}T00:00:00Z`);return Number.isFinite(d.getTime())&&d.toISOString().startsWith(v)&&d.getUTCFullYear()>=1800&&d.getUTCFullYear()<=2100;},'Invalid premiere date');
export const importMetadataDocument=z.object({
 title:z.string().min(1).max(200).refine(v=>v.trim().length>0),
 originalTitle:z.string().max(200).nullable().optional(),
 sortTitle:z.string().max(200).nullable().optional(),
 overview:z.string().max(10000).nullable().optional(),
 productionYear:z.number().int().min(1800).max(2100).nullable().optional(),
 premiereDate:date.nullable().optional(),
 studios:z.array(z.string().min(1).max(120)).max(20).default([]),
 genres:z.array(z.string().min(1).max(80)).max(20).default([]),
 providerIds:z.object({imdb:z.string().regex(/^tt[0-9]{4,12}$/).optional(),tmdb:z.string().regex(/^[1-9][0-9]{0,11}$/).optional(),tvdb:z.string().regex(/^[1-9][0-9]{0,11}$/).optional()}).strict().default({})
}).strict().refine(v=>!v.productionYear||!v.premiereDate||String(v.productionYear)===v.premiereDate.slice(0,4),'Premiere date must agree with production year');
const indexedDocument=z.object({
 title:z.string().max(512),originalTitle:z.string().max(512).nullable().optional(),sortTitle:z.string().max(512).nullable().optional(),overview:z.string().max(100000).nullable().optional(),productionYear:z.number().int().nullable().optional(),premiereDate:z.string().nullable().optional(),studios:z.array(z.string().max(512)).max(100).default([]),genres:z.array(z.string().max(512)).max(100).default([]),providerIds:z.record(z.string().max(40),z.string().max(512)).default({})
}).strict();
const response=z.object({jobId:importJobId,revision:metadataRevision,revisions:z.array(metadataRevision).max(100),persistedMetadata:indexedDocument.nullable(),indexedMetadata:indexedDocument.nullable(),artwork:z.object({primary:z.boolean(),backdrop:z.boolean()}).strict(),verified:z.boolean(),refreshStatus:z.enum(['not-requested','completed','pending','mismatch'])}).strict();
const maximumArtwork=8*1024**2;
async function supports(jellyfin){
 const c=await inspectUploadPlugin(jellyfin);
 if(c.metadataProtocol!=='watermelonfin-metadata.v1'||c.ownedImportMetadata!==true||c.metadataRestore!==true||!Number.isSafeInteger(c.maxArtworkBytes)||c.maxArtworkBytes<1||c.maxArtworkBytes>maximumArtwork||!Array.isArray(c.artworkRoles)||!['primary','backdrop'].every(r=>c.artworkRoles.includes(r)))throw new Error('Native plugin upgrade required for owned import metadata support');
 return c;
}
function comparable(d){return JSON.stringify({title:d.title,originalTitle:d.originalTitle||null,sortTitle:d.sortTitle||null,overview:d.overview||null,productionYear:d.productionYear??null,premiereDate:d.premiereDate??null,studios:d.studios??[],genres:d.genres??[],providerIds:Object.fromEntries(Object.entries(d.providerIds??{}).map(([k,v])=>[k.toLowerCase(),v]).sort(([a],[b])=>a.localeCompare(b)))});}
function readResult(jobId,value){
 const parsed=response.safeParse(value);if(!parsed.success||parsed.data.jobId!==jobId)throw new Error('Invalid plugin metadata response');
 const result=parsed.data;
 if(result.verified&&(result.refreshStatus==='pending'||!result.persistedMetadata||!result.indexedMetadata||comparable(result.persistedMetadata)!==comparable(result.indexedMetadata)))throw new Error('Plugin returned inconsistent metadata verification');
 return result;
}
export async function inspectImportMetadata(jellyfin,jobId){
 importJobId.parse(jobId);await supports(jellyfin);return readResult(jobId,await jellyfin.pluginRequest(`Jobs/${jobId}/Metadata`));
}
export async function updateImportMetadata(jellyfin,jobId,expectedRevision,metadata){
 importJobId.parse(jobId);metadataRevision.parse(expectedRevision);const document=importMetadataDocument.parse(metadata);await supports(jellyfin);
 const result=readResult(jobId,await jellyfin.pluginRequest(`Jobs/${jobId}/Metadata`,'POST',JSON.stringify({expectedRevision,metadata:document})));
 if(!result.persistedMetadata||comparable(document)!==comparable(result.persistedMetadata))throw new Error('Plugin did not persist the requested metadata');return result;
}
export async function setImportArtwork(jellyfin,jobId,expectedRevision,role,path,roots){
 importJobId.parse(jobId);metadataRevision.parse(expectedRevision);artworkRole.parse(role);artworkPath.parse(path);
 const {handle}=await openSelectedFile(path,roots);let bytes;
 try{
  const before=await handle.stat();if(before.size<24||before.size>maximumArtwork)throw new Error('Artwork must be a JPEG or PNG no larger than 8 MiB');
  bytes=Buffer.alloc(before.size);let offset=0;while(offset<bytes.length){const {bytesRead}=await handle.read(bytes,offset,bytes.length-offset,offset);if(!bytesRead)throw new Error('Artwork file changed during read');offset+=bytesRead;}
  const after=await handle.stat();if(after.size!==before.size||after.mtimeMs!==before.mtimeMs)throw new Error('Artwork file changed during read');
  const png=bytes.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10]));const jpeg=bytes[0]===255&&bytes[1]===216&&bytes.at(-2)===255&&bytes.at(-1)===217;
  if(!png&&!jpeg)throw new Error('Artwork must be JPEG or PNG content');
 }finally{await handle.close();}
 const c=await supports(jellyfin);if(bytes.length>c.maxArtworkBytes)throw new Error('Artwork exceeds the native plugin limit');
 const result=readResult(jobId,await jellyfin.pluginRequest(`Jobs/${jobId}/Artwork/${role}?expectedRevision=${expectedRevision}`,'POST',bytes));
 if(result.verified&&!result.artwork[role])throw new Error('Plugin artwork verification is inconsistent');return result;
}
export async function restoreImportMetadata(jellyfin,jobId,expectedRevision,revision){
 importJobId.parse(jobId);metadataRevision.parse(expectedRevision);metadataRevision.parse(revision);await supports(jellyfin);
 return readResult(jobId,await jellyfin.pluginRequest(`Jobs/${jobId}/Metadata/Restore`,'POST',JSON.stringify({expectedRevision,revision})));
}
export async function refreshImportMetadata(jellyfin,jobId,expectedRevision){
 importJobId.parse(jobId);metadataRevision.parse(expectedRevision);await supports(jellyfin);
 return readResult(jobId,await jellyfin.pluginRequest(`Jobs/${jobId}/Metadata/Refresh`,'POST',JSON.stringify({expectedRevision})));
}
