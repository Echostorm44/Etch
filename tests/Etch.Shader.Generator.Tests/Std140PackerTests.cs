using System;
using System.Collections.Generic;
using Etch.Shader.Generator;

namespace Etch.Shader.Generator.Tests;

internal sealed class Std140PackerTests
{
    [Test]
    public void GetAlignment_ScalarTypes()
    {
        if (Std140Packer.GetAlignment("f32") != 4)
        {
            throw new InvalidOperationException("f32 alignment should be 4");
        }
        if (Std140Packer.GetAlignment("u32") != 4)
        {
            throw new InvalidOperationException("u32 alignment should be 4");
        }
        if (Std140Packer.GetAlignment("i32") != 4)
        {
            throw new InvalidOperationException("i32 alignment should be 4");
        }
    }

    [Test]
    public void GetAlignment_Vec2()
    {
        if (Std140Packer.GetAlignment("vec2<f32>") != 8)
        {
            throw new InvalidOperationException("vec2 alignment should be 8");
        }
    }

    [Test]
    public void GetAlignment_Vec3()
    {
        if (Std140Packer.GetAlignment("vec3<f32>") != 16)
        {
            throw new InvalidOperationException("vec3 alignment should be 16");
        }
    }

    [Test]
    public void GetAlignment_Vec4()
    {
        if (Std140Packer.GetAlignment("vec4<f32>") != 16)
        {
            throw new InvalidOperationException("vec4 alignment should be 16");
        }
    }

    [Test]
    public void GetBaseSize_F32()
    {
        if (Std140Packer.GetBaseSize("f32") != 4)
        {
            throw new InvalidOperationException("f32 size should be 4");
        }
    }

    [Test]
    public void GetBaseSize_Vec3()
    {
        if (Std140Packer.GetBaseSize("vec3<f32>") != 12)
        {
            throw new InvalidOperationException("vec3 size should be 12");
        }
    }

    [Test]
    public void GetBaseSize_Mat3x3()
    {
        if (Std140Packer.GetBaseSize("mat3x3<f32>") != 48)
        {
            throw new InvalidOperationException("mat3x3 size should be 48 (3 * 16)");
        }
    }

    [Test]
    public void ComputeStructSize_SimpleStruct()
    {
        var fields = new List<WgslTokenizer.StructField>
        {
            new WgslTokenizer.StructField("x", "f32"),
            new WgslTokenizer.StructField("y", "f32")
        };

        int size = Std140Packer.ComputeStructSize(fields);
        if (size != 8)
        {
            throw new InvalidOperationException($"Two f32 fields should be 8 bytes, got {size}");
        }
    }

    [Test]
    public void ComputeStructSize_Vec3PlusF32()
    {
        var fields = new List<WgslTokenizer.StructField>
        {
            new WgslTokenizer.StructField("position", "vec3<f32>"),
            new WgslTokenizer.StructField("alpha", "f32")
        };

        // WGSL memory layout: vec3<f32> has AlignOf 16 but SizeOf 12, so a following f32 (align 4)
        // fills the trailing 4 bytes at offset 12. Struct size = roundUp(AlignOf(struct) = 16, 16) = 16.
        int size = Std140Packer.ComputeStructSize(fields);
        if (size != 16)
        {
            throw new InvalidOperationException($"vec3<f32> (12 bytes, align 16) + f32 at offset 12 = 16, got {size}");
        }
    }

    [Test]
    public void ComputeFieldOffsets_Vec3PlusF32()
    {
        var fields = new List<WgslTokenizer.StructField>
        {
            new WgslTokenizer.StructField("position", "vec3<f32>"),
            new WgslTokenizer.StructField("alpha", "f32")
        };

        var offsets = Std140Packer.ComputeFieldOffsets(fields);
        if (offsets["position"] != 0)
        {
            throw new InvalidOperationException($"position offset should be 0, got {offsets["position"]}");
        }
        // WGSL memory layout: vec3<f32> occupies 12 bytes, and f32 only needs 4-byte alignment, so
        // it packs directly after the vec3 at offset 12 (the same placement std140 uses).
        if (offsets["alpha"] != 12)
        {
            throw new InvalidOperationException($"alpha offset should be 12 (packed into the vec3's trailing 4 bytes), got {offsets["alpha"]}");
        }
    }

    [Test]
    public void ComputeStructSize_Mat3x3()
    {
        var fields = new List<WgslTokenizer.StructField>
        {
            new WgslTokenizer.StructField("transform", "mat3x3<f32>")
        };

        int size = Std140Packer.ComputeStructSize(fields);
        if (size != 48)
        {
            throw new InvalidOperationException($"mat3x3 size should be 48, got {size}");
        }
    }
}