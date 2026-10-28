// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;

namespace WatermelonFin.Plugin.Core;

public static class MetadataArtwork
{
    public const int MaximumBytes=8*1024*1024;
    public static string Validate(byte[] bytes)
    {
        if(bytes.Length<24 || bytes.Length>MaximumBytes) throw new UploadException("Invalid artwork size");
        static void Dimensions(uint w,uint h) { if(w==0 || h==0 || w>8192 || h>8192 || (long)w*h>40_000_000)throw new UploadException("Artwork dimensions exceed limits"); }
        if(bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))
        {
            var offset=8;var header=false;var data=false;var end=false;
            while(offset<=bytes.Length-12)
            {
                var length=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset,4));
                if(length>MaximumBytes || (long)offset+12+length>bytes.Length)throw new UploadException("Malformed PNG artwork");
                var type=System.Text.Encoding.ASCII.GetString(bytes,offset+4,4);
                uint crc=0xffffffff;
                for(var c=offset+4;c<offset+8+(int)length;c++)
                {crc^=bytes[c];for(var bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}
                if((crc^0xffffffff)!=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset+8+(int)length,4)))throw new UploadException("PNG checksum mismatch");
                if(!header) { if(type!="IHDR" || length!=13)throw new UploadException("Malformed PNG header");Dimensions(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset+8,4)),BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset+12,4)));header=true; }
                else if(type=="IHDR")throw new UploadException("Duplicate PNG header");
                if(type=="IDAT")data|=length>0;
                offset+=checked((int)length)+12;
                if(type=="IEND") { if(length!=0 || offset!=bytes.Length)throw new UploadException("Malformed PNG end");end=true;break; }
            }
            if(!header || !data || !end)throw new UploadException("Incomplete PNG artwork");
            return "png";
        }
        if(bytes[0]==0xff && bytes[1]==0xd8 && bytes[^2]==0xff && bytes[^1]==0xd9)
        {
            var i=2;var frame=false;
            while(i<bytes.Length-2)
            {
                if(bytes[i++]!=0xff)throw new UploadException("Malformed JPEG artwork");
                while(i<bytes.Length && bytes[i]==0xff)i++;
                if(i>=bytes.Length-2)break;
                var marker=bytes[i++];
                if(marker==0xda) { if(!frame || i+2>=bytes.Length)throw new UploadException("Malformed JPEG scan");return "jpg"; }
                if(marker is 0xd8 or 0xd9 or 0x00)throw new UploadException("Malformed JPEG marker");
                if(i+2>bytes.Length)throw new UploadException("Malformed JPEG segment");
                var size=BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i,2));
                if(size<2 || i+size>bytes.Length-2)throw new UploadException("Malformed JPEG length");
                if(marker is 0xc0 or 0xc1 or 0xc2)
                { if(size<8)throw new UploadException("Malformed JPEG frame");Dimensions(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i+5,2)),BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i+3,2)));frame=true; }
                i+=size;
            }
        }
        throw new UploadException("Only structurally valid bounded JPEG and PNG artwork is accepted");
    }
}
