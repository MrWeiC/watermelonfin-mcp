#!/usr/bin/env python3
"""Package a built DLL; emit an install catalog only with a real hosted ZIP URL."""
import argparse,hashlib,json,zipfile
from pathlib import Path
from urllib.parse import urlparse

root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--source-url',help='Actual public/private-server-accessible HTTPS URL of the published plugin ZIP');args=p.parse_args()
binary=root/'plugin/WatermelonFin.Plugin/bin/Release/net10.0/WatermelonFin.Plugin.dll'
if not binary.is_file():p.error('Build the plugin before packaging')
out=root/'artifacts';out.mkdir(exist_ok=True)
package=out/'WatermelonFin.Upload-0.1.2.0.zip'
with zipfile.ZipFile(package,'w',zipfile.ZIP_DEFLATED) as z:
    info=zipfile.ZipInfo('WatermelonFin.Plugin.dll',date_time=(2026,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o644<<16;z.writestr(info,binary.read_bytes())
    for name,path in [('LICENSE',root/'plugin/WatermelonFin.Plugin/LICENSE'),('THIRD_PARTY_NOTICES.md',root/'THIRD_PARTY_NOTICES.md')]:
        item=zipfile.ZipInfo(name,date_time=(2026,1,1,0,0,0));item.compress_type=zipfile.ZIP_DEFLATED;item.external_attr=0o644<<16;z.writestr(item,path.read_bytes())
data=package.read_bytes();md5=hashlib.md5(data).hexdigest();sha256=hashlib.sha256(data).hexdigest()
(out/(package.name+'.sha256')).write_text(sha256+'  '+package.name+'\n')
release={'name':'WatermelonFin Upload','version':'0.1.2.0','targetAbi':'12.4.0.0','package':package.name,'checksum':md5,'sha256':sha256,'catalogReady':False}
if args.source_url:
    u=urlparse(args.source_url)
    if u.scheme!='https' or not u.hostname or u.username or u.password or u.fragment:p.error('Use a real HTTPS download URL without credentials or fragment')
    catalog=[{'category':'General','guid':'cbe3ae89-5d55-4e89-bb49-cb09af9b9bfe','name':'WatermelonFin Upload','description':'Additive uploads for WatermelonFin MCP; no delete or overwrite API.','owner':'WatermelonFin','overview':'Restricted native media uploads','versions':[{'version':'0.1.2.0','targetAbi':'12.4.0.0','sourceUrl':args.source_url,'checksum':md5,'changelog':'Initial restricted native upload plugin.'}]}]
    (out/'manifest.json').write_text(json.dumps(catalog,indent=2)+'\n');release['catalogReady']=True
(out/'release-info.json').write_text(json.dumps(release,indent=2)+'\n')
print(json.dumps(release,indent=2))
