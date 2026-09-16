Imports OpenTK.Mathematics

''' <summary>
''' Packing a normal or tangent into GL_INT_2_10_10_10_REV.
'''
''' LIFTED OUT OF modOpenGL so it can be linked on its own. It is pure
''' arithmetic with no GL state, but it sat in a 722-line module whose 2D
''' helpers reach for MainFBO and two shaders - so anything wanting just
''' this had to drag the framebuffer and shader systems in behind it.
'''
''' Brain Testing needs it because ChunkFunctions packs every terrain
''' vertex normal and tangent through it. Copying the ten lines instead
''' was the alternative, and a copy that drifted would corrupt normals
''' quietly rather than fail - there is no error, only wrong shading.
'''
''' Moved 2026-09-15 by nuTerra work, unchanged.
''' </summary>
Module modVertexPack

    Private Function pack_10(x As Single) As UInt32
        Dim qx As Int32 = MathHelper.Clamp(CType(x * 511.0F, Int32), -512, 511)
        If qx < 0 Then
            Return (1 << 9) Or ((CType(-1 - qx, UInt32) Xor ((1 << 9) - 1)))
        Else
            Return qx
        End If
    End Function

    Public Function pack_2_10_10_10(unpacked As Vector3, Optional w As UInt32 = 0) As UInt32
        unpacked.Normalize()

        Dim packed_x As UInt32 = pack_10(unpacked.X)
        Dim packed_y As UInt32 = pack_10(unpacked.Y)
        Dim packed_z As UInt32 = pack_10(unpacked.Z)
        Return packed_x Or (packed_y << 10) Or (packed_z << 20) Or (w << 30)
    End Function

End Module
