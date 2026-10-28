#!/usr/bin/env python3
"""Package a built DLL; emit an install catalog only with a real hosted ZIP URL."""
import argparse,hashlib,json,zipfile,re
import xml.etree.ElementTree as ET
from pathlib import Path
from urllib.parse import urlparse

root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--source-url',help='Actual public/private-server-accessible HTTPS URL of the published plugin ZIP');p.add_argument('--image-url',help='Actual HTTPS URL of the published WatermelonFin logo for the plugin catalog');args=p.parse_args()
if bool(args.source_url)!=bool(args.image_url):p.error('Catalog generation requires both --source-url and --image-url')
for value in [args.source_url,args.image_url]:
    if value:
        u=urlparse(value)
        if u.scheme!='https' or not u.hostname or u.username or u.password or u.fragment:p.error('Use real HTTPS asset URLs without credentials or fragments')
binary=root/'plugin/WatermelonFin.Plugin/bin/Release/net10.0/WatermelonFin.Plugin.dll'
if not binary.is_file():p.error('Build the plugin before packaging')
version=ET.parse(root/'plugin/WatermelonFin.Plugin/WatermelonFin.Plugin.csproj').findtext('./PropertyGroup/Version')
if not version or not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+',version):p.error('Use a four-part numeric plugin version')
logo=root/'assets/logo.png'
if not logo.is_file():p.error('Bundled WatermelonFin logo is missing')
out=root/'artifacts';out.mkdir(exist_ok=True)
(out/'watermelonfin-logo.png').write_bytes(logo.read_bytes())
metadata={'category':'General','changelog':'Owned-import metadata repair and WatermelonFin branding.','description':'Additive uploads and owned-import metadata repair; no media deletion or video overwrite.','guid':'cbe3ae89-5d55-4e89-bb49-cb09af9b9bfe','name':'WatermelonFin Upload','overview':'Restricted video imports and metadata repair','owner':'WatermelonFin','targetAbi':'12.4.0.0','version':version,'status':'Active','imagePath':'logo.png','assemblies':['WatermelonFin.Plugin.dll']}
package=out/f'WatermelonFin.Upload-{version}.zip'
with zipfile.ZipFile(package,'w',zipfile.ZIP_DEFLATED) as z:
    info=zipfile.ZipInfo('WatermelonFin.Plugin.dll',date_time=(2026,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o644<<16;z.writestr(info,binary.read_bytes())
    for name,content in [('logo.png',logo.read_bytes()),('meta.json',(json.dumps(metadata,indent=2)+'\n').encode())]:
        item=zipfile.ZipInfo(name,date_time=(2026,1,1,0,0,0));item.compress_type=zipfile.ZIP_DEFLATED;item.external_attr=0o644<<16;z.writestr(item,content)
    for name,path in [('LICENSE',root/'plugin/WatermelonFin.Plugin/LICENSE'),('THIRD_PARTY_NOTICES.md',root/'THIRD_PARTY_NOTICES.md')]:
        item=zipfile.ZipInfo(name,date_time=(2026,1,1,0,0,0));item.compress_type=zipfile.ZIP_DEFLATED;item.external_attr=0o644<<16;z.writestr(item,path.read_bytes())
data=package.read_bytes();md5=hashlib.md5(data).hexdigest();sha256=hashlib.sha256(data).hexdigest()
(out/(package.name+'.sha256')).write_text(sha256+'  '+package.name+'\n')
release={'name':'WatermelonFin Upload','version':version,'targetAbi':'12.4.0.0','package':package.name,'checksum':md5,'sha256':sha256,'catalogReady':False,'logo':'watermelonfin-logo.png'}
if args.source_url:
    u=urlparse(args.source_url)
    if u.scheme!='https' or not u.hostname or u.username or u.password or u.fragment:p.error('Use a real HTTPS download URL without credentials or fragment')
    catalog=[{'category':'General','guid':'cbe3ae89-5d55-4e89-bb49-cb09af9b9bfe','name':'WatermelonFin Upload','description':'Additive uploads and owned-import metadata repair; no media deletion or video overwrite.','owner':'WatermelonFin','overview':'Restricted native media uploads and metadata repair','imageUrl':args.image_url,'versions':[{'version':version,'targetAbi':'12.4.0.0','sourceUrl':args.source_url,'checksum':md5,'changelog':'Adds revisioned metadata/artwork repair limited to completed imports owned by the authenticated restricted user.'}]}]
    (out/'manifest.json').write_text(json.dumps(catalog,indent=2)+'\n');release['catalogReady']=True
(out/'release-info.json').write_text(json.dumps(release,indent=2)+'\n')
print(json.dumps(release,indent=2))
