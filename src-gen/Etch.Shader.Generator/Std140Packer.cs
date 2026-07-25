using System;
using System.Collections.Generic;
using System.Text;

namespace Etch.Shader.Generator;

internal static class Std140Packer
{
    public static int ComputeSize(string wgslType, uint arrayCount = 0)
    {
        int elementSize = GetBaseSize(wgslType);
        if (arrayCount > 0)
        {
            int alignment = GetAlignment(wgslType);
            int stride = ((elementSize + alignment - 1) / alignment) * alignment;
            return (int)arrayCount * stride;
        }
        return elementSize;
    }

    public static int GetAlignment(string wgslType)
    {
        if (wgslType.StartsWith("vec2", StringComparison.Ordinal)) return 8;
        if (wgslType.StartsWith("vec3", StringComparison.Ordinal)) return 16;
        if (wgslType.StartsWith("vec4", StringComparison.Ordinal)) return 16;
        if (wgslType.StartsWith("mat2", StringComparison.Ordinal)) return 8;
        if (wgslType.StartsWith("mat3", StringComparison.Ordinal)) return 16;
        if (wgslType.StartsWith("mat4", StringComparison.Ordinal)) return 16;
        if (wgslType == "f32" || wgslType == "u32" || wgslType == "i32") return 4;
        if (wgslType == "f64") return 8;
        return 4;
    }

    public static int GetBaseSize(string wgslType)
    {
        switch (wgslType)
        {
            case "f32":
            case "u32":
            case "i32":
                return 4;
            case "f64":
                return 8;
            case "vec2<f32>":
            case "vec2<u32>":
            case "vec2<i32>":
                return 8;
            case "vec3<f32>":
            case "vec3<u32>":
            case "vec3<i32>":
                return 12;
            case "vec4<f32>":
            case "vec4<u32>":
            case "vec4<i32>":
                return 16;
            case "mat2x2<f32>":
                return 16;
            case "mat3x3<f32>":
                return 48;
            case "mat4x4<f32>":
                return 64;
            case "mat2x3<f32>":
                return 24;
            case "mat3x2<f32>":
                return 32;
            case "mat2x4<f32>":
                return 32;
            case "mat4x2<f32>":
                return 16;
            case "mat3x4<f32>":
                return 64;
            case "mat4x3<f32>":
                return 48;
            default:
                if (wgslType.EndsWith('>'))
                {
                    return ComputeSize(wgslType);
                }
                return 4;
        }
    }

    public static string GetCsType(string wgslType)
    {
        return wgslType switch
        {
            "f32" => "float",
            "u32" => "uint",
            "i32" => "int",
            "f64" => "double",
            "vec2<f32>" => "Vector2",
            "vec3<f32>" => "Vector3",
            "vec4<f32>" => "Vector4",
            "vec2<u32>" => "UInt2",
            "vec3<u32>" => "UInt3",
            "vec4<u32>" => "UInt4",
            "vec2<i32>" => "Int2",
            "vec3<i32>" => "Int3",
            "vec4<i32>" => "Int4",
            "mat3x3<f32>" => "Matrix3x3Aligned16",
            "mat4x4<f32>" => "Matrix4x4Aligned16",
            _ => "Vector4"
        };
    }

    public static int ComputeStructSize(List<WgslTokenizer.StructField> fields)
    {
        int offset = 0;
        foreach (var field in fields)
        {
            int alignment = GetAlignment(field.Type);
            int alignedOffset = ((offset + alignment - 1) / alignment) * alignment;
            offset = alignedOffset + ComputeSize(field.Type, field.ArrayCount);
        }
        int structAlignment = GetStructAlignment(fields);
        return ((offset + structAlignment - 1) / structAlignment) * structAlignment;
    }

    public static int GetStructAlignment(List<WgslTokenizer.StructField> fields)
    {
        int maxAlignment = 4;
        foreach (var field in fields)
        {
            int alignment = GetAlignment(field.Type);
            if (alignment > maxAlignment)
                maxAlignment = alignment;
        }
        return maxAlignment;
    }

    public static Dictionary<string, int> ComputeFieldOffsets(List<WgslTokenizer.StructField> fields)
    {
        var offsets = new Dictionary<string, int>();
        int offset = 0;
        foreach (var field in fields)
        {
            int alignment = GetAlignment(field.Type);
            int alignedOffset = ((offset + alignment - 1) / alignment) * alignment;
            offsets[field.Name] = alignedOffset;
            offset = alignedOffset + ComputeSize(field.Type, field.ArrayCount);
        }
        return offsets;
    }
}