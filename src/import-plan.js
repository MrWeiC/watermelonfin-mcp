const allowed=new Set(['.mkv','.mp4','.m4v','.mov','.avi','.mp3','.m4a','.flac','.wav']);
export function safeName(name){
 if(typeof name!=='string'||name.length<1||name.length>180||/[\\/\x00-\x1f\x7f<>:"|?*]/.test(name)||name==='.'||name==='..'||/[. ]$/.test(name)||/^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(name))throw new Error('Invalid media filename');
 return name;
}
export function planImport(fileName){
 safeName(fileName);const dot=fileName.lastIndexOf('.');const ext=fileName.slice(dot).toLowerCase();
 if(!allowed.has(ext))throw new Error('Unsupported media extension');
 const stem=fileName.slice(0,dot);const ep=/^(.+?)[ ._-]+S(\d{2})E(\d{2})(?:[ ._-].*)?$/i.exec(stem);
 const clean=s=>safeName(s.replace(/[._]/g,' ').replace(/\s+/g,' ').trim());
 if(ep){const title=clean(ep[1]);return {status:'ready',kind:'tv',relativePath:`TV/${title}/Season ${ep[2]}/${title} - S${ep[2]}E${ep[3]}${ext}`};}
 const movie=/^(.+?)\s*\((19\d{2}|20\d{2})\)$/.exec(stem);
 if(movie){const title=`${clean(movie[1])} (${movie[2]})`;return {status:'ready',kind:'movies',relativePath:`Movies/${title}/${title}${ext}`};}
 return {status:'needs_clarification',fileName,reason:'Supply an unambiguous title with year, or SxxExx episode name. This planner never moves existing media.'};
}
