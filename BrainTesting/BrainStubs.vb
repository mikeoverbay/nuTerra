Imports OpenTK.Mathematics

''' <summary>
''' STANDING IN FOR THINGS BRAIN TESTING DOES NOT HAVE.
'''
''' Every type here is a LIE the app tells itself, so every one says which
''' real file it replaces and why that was cheaper than linking it. If a stub
''' ever needs a method body, that is the signal it should have been the real
''' file all along.
'''
''' Why any of this is needed: VB compiles all-or-nothing, so a linked file
''' drags in every type it NAMES - even on a field nothing in this app ever
''' reads. TankVehicle.vb declares four such fields, and the alternative to
''' stubbing them was linking the brain and the combat model to satisfy four
''' declarations.
'''
''' Added 2026-09-15 by nuTerra work, stage 4 of docs/brain_testing_plan.md.
''' </summary>

''' <summary>
''' Stands in for nuTerra/Tanks/TankDrive.vb - 885 lines, and THE OLD BRAIN.
'''
''' This is the most important stub in the file. "the brain will be new. I
''' need the world" - the owner, 2026-09-15. TankInstance.drive is where the
''' old AI kept its steering state; Brain Testing fields the same hulls and
''' hands them to something written fresh, so the field exists to satisfy the
''' declaration and is never read. The new brain keeps its own state its own
''' way - see "The seam" in docs/brain_testing_plan.md.
'''
''' EMPTY ON PURPOSE. If something here starts reading `inst.drive`, the old
''' brain has crept back in and that is a bug, not a convenience.
''' </summary>
Public Class TankDrive
End Class

''' <summary>Stands in for the recoil cycle in nuTerra/Tanks/TankRecoil.vb.
''' A gun kicking when it fires is a rendering concern and nothing is firing
''' here.</summary>
Public Class TankRecoil
End Class

''' <summary>Stands in for nuTerra/Tanks/TankShotPool.vb. No shells, no
''' shooting - the AI question is "can it get there", not "can it hit".</summary>
Public Class TankShotPool
End Class

''' <summary>
''' Stands in for nuTerra/TextureEngine/TextureMgr.vb - 856 lines of DDS
''' decoding and upload.
'''
''' NOT REALLY A LIE, THIS ONE - it is the requirement. "the tanks have no
''' textures", the owner, 2026-09-15. Returning Nothing IS "no texture", and
''' the material binder already tests every map for Nothing before binding
''' it, so nothing downstream has to change.
'''
''' It also makes the load much cheaper, which is the point of the harness:
''' thirty tier 10 vehicles otherwise decode and upload several hundred
''' megabytes of DDS that nothing will ever sample.
''' </summary>
Public Module TextureMgr
    ''' <summary>No texture, by design. The signature matches the real one so
    ''' the linked TankFiles compiles unchanged.</summary>
    Public Function load_dds_image_from_stream(ms As IO.MemoryStream, name As String) As GLTexture
        Return Nothing
    End Function

    ''' <summary>The terrain and UI loaders TerrainBuilder calls. All return
    ''' Nothing for the same reason: no textures. Every caller assigns the
    ''' result to a field nothing binds.</summary>
    Public Function find_and_load_texture_from_pkgs(ByRef fn As String) As GLTexture
        Return Nothing
    End Function

    Public Function find_and_load_texture_from_pkgs_No_Suffix_change(
            ByRef fn As String, heightMap As Boolean) As GLTexture
        Return Nothing
    End Function

    ''' <summary>Enough of a DDS header for the one linked caller that
    ''' reads it - TerrainBuilder's SH probe grid loader, which checks the
    ''' FourCC and gives up. Returning a header whose FourCC matches
    ''' nothing makes it give up, which is the wanted outcome.</summary>
    Public Function get_dds_header(br As IO.BinaryReader) As DDSHeader
        Return New DDSHeader
    End Function

    Public Function find_and_load_UI_texture_from_pkgs(path As String) As GLTexture
        Return Nothing
    End Function
End Module

''' <summary>Stands in for the DDS header in TextureMgr. Only the fields
''' the linked terrain code reads.</summary>
Public Class DDSHeader
    Public width As Integer = 0
    Public height As Integer = 0
    Public depth As Integer = 0
    Public dxgiFormat As UInteger = 0
    Public data_start As Integer = 128
    ''' <summary>Deliberately not "q" & 3 nulls, which is what the probe
    ''' grid loader requires - so it reports a non-RGBA16F grid and
    ''' returns, which is what this app wants it to do.</summary>
    Public FourCC As String = ""
End Class


''' <summary>Stands in for nuTerra/MapLoader/TerrainTextureFunctions.vb -
''' the per-chunk terrain texture LAYERS. No textures here, so the layers
''' are not read.</summary>
Public Module TerrainTextureFunctions
    Public Sub get_layers(map As Integer)
    End Sub
End Module

''' <summary>
''' Stands in for the struct in nuTerra/OpenGL/modOpenGL.vb.
'''
''' Stubbed rather than linking that module: its 2D helpers reach for
''' MainFBO, rect2dShader and image2dShader, which pulls the framebuffer
''' and shader systems in behind a struct of five integers. The one thing
''' from there worth having - pack_2_10_10_10 - was lifted into
''' modVertexPack instead, and IS linked, because a hand copy of a bit
''' packer would corrupt normals silently rather than fail.
'''
''' Layout matches glDrawElementsIndirect's command exactly; the outland
''' builders fill an array of these and upload it.
''' </summary>
Public Structure DrawElementsIndirectCommand
    Public count As UInteger
    Public instanceCount As UInteger
    Public firstIndex As UInteger
    Public baseVertex As UInteger
    Public baseInstance As UInteger
End Structure
