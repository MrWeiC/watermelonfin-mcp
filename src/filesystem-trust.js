import {lstat,realpath} from 'node:fs/promises';
import {dirname,resolve} from 'node:path';
// Portable Node does not expose openat-style anchored traversal. Require private
// directories and trust same-UID processes; never advertise a sandbox against them.
export async function assertPrivateDirectory(path){
 const s=await lstat(path);
 if(!s.isDirectory()||s.isSymbolicLink()||(s.mode&0o022)!==0||(process.getuid&&s.uid!==process.getuid()))throw new Error('Directory must have private ownership and no group/world write permissions');
 return s;
}
export async function assertTrustedAncestors(path){
 let p=resolve(path);
 while(true){const s=await lstat(p);if(!s.isDirectory()||s.isSymbolicLink())throw new Error('Symlink ancestry refused');
 // Sticky parent directories (e.g. /private/tmp) protect owned children from other UIDs.
 if((s.mode&0o022)!==0&&(s.mode&0o1000)===0)throw new Error('Shared writable directory ancestry refused');
 const next=dirname(p);if(next===p)break;p=next;
 }
}
export async function verifyDirectoryIdentity(path,expected){
 const actual=await assertPrivateDirectory(path);
 if(actual.ino!==expected.ino||actual.dev!==expected.dev||await realpath(path)!==path)throw new Error('Directory identity changed');
}
