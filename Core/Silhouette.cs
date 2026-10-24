using System;
using System.Collections.Generic;

namespace NGarage.Core;

/// <summary>Finds a car's outside shape in its picture: everything inside the car's outline, with the
/// backdrop and the darker ground shadow outside. Pictures have a plain dark backdrop (its color is read
/// from the top-left corner) and the car may have dark grilles, trim and tires that touch the shadow, so
/// a pixel counts as inside only when it lies between the outermost car pixels of both its row and its
/// column.</summary>
public static class Silhouette
{
    // rgb: tightly packed 8-bit RGB. Returns 1 inside the car, 0 outside.
    public static byte[] Mask(ReadOnlySpan<byte> rgb,int width,int height)
    {
        if(width<3||height<3||rgb.Length<width*height*3)throw new ArgumentException("Picture is too small.");
        int br=rgb[0],bg=rgb[1],bb=rgb[2];
        var car=new byte[width*height];
        for(int p=0;p<car.Length;p++) {
            int r=rgb[p*3],g=rgb[p*3+1],b=rgb[p*3+2];
            bool near=Math.Abs(r-br)<=7&&Math.Abs(g-bg)<=7&&Math.Abs(b-bb)<=7;
            bool darker=r<=br+2&&g<=bg+2&&b<=bb+2;
            car[p]=(byte)(near||darker?0:1);
        }
        var left=new int[height];var right=new int[height];var top=new int[width];var bottom=new int[width];
        Array.Fill(left,width);Array.Fill(right,-1);Array.Fill(top,height);Array.Fill(bottom,-1);
        for(int y=0;y<height;y++)for(int x=0;x<width;x++) {
            if(car[y*width+x]==0)continue;
            if(x<left[y])left[y]=x;right[y]=x;
            if(y<top[x])top[x]=y;bottom[x]=y;
        }
        var inside=new byte[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            inside[y*width+x]=(byte)(x>=left[y]&&x<=right[y]&&y>=top[x]&&y<=bottom[x]?1:0);
        // Stray light pixels in the shadow stretch a column into a thin spike; trim anything under 5 pixels across,
        // then keep only the car itself, the largest connected shape.
        return Largest(Grow(Grow(inside,width,height,2,false),width,height,2,true),width,height);
    }
    private static byte[] Largest(byte[] mask,int width,int height)
    {
        var label=new int[mask.Length];var stack=new Stack<int>();
        int best=0,bestSize=0,next=0;
        for(int start=0;start<mask.Length;start++) {
            if(mask[start]==0||label[start]!=0)continue;
            int id=++next,size=0;label[start]=id;stack.Push(start);
            while(stack.Count>0) {
                int p=stack.Pop();size++;
                int x=p%width,y=p/width;
                foreach(int n in new[]{x>0?p-1:-1,x<width-1?p+1:-1,y>0?p-width:-1,y<height-1?p+width:-1})
                    if(n>=0&&mask[n]==1&&label[n]==0){label[n]=id;stack.Push(n);}
            }
            if(size>bestSize){best=id;bestSize=size;}
        }
        var result=new byte[mask.Length];
        for(int p=0;p<mask.Length;p++)result[p]=(byte)(label[p]==best&&best!=0?1:0);
        return result;
    }
    // Square dilation (grow=true) or erosion by radius, done as separate row and column passes.
    private static byte[] Grow(byte[] mask,int width,int height,int radius,bool grow)
    {
        byte want=(byte)(grow?1:0);
        var rows=new byte[mask.Length];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++) {
            byte v=(byte)(1-want);
            for(int d=-radius;d<=radius&&v!=want;d++){int xx=Math.Clamp(x+d,0,width-1);if(mask[y*width+xx]==want)v=want;}
            rows[y*width+x]=v;
        }
        var result=new byte[mask.Length];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++) {
            byte v=(byte)(1-want);
            for(int d=-radius;d<=radius&&v!=want;d++){int yy=Math.Clamp(y+d,0,height-1);if(rows[yy*width+x]==want)v=want;}
            result[y*width+x]=v;
        }
        return result;
    }
}
