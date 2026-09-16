Imports OpenTK.Mathematics

''' <summary>
''' THE SCENE THE LINKED BUILDERS FILL IN.
'''
''' nuTerra's TerrainBuilder and ChunkFunctions write everything they make
''' into `map_scene.terrain.*` - the VAOs, the vertex and index buffers, the
''' outland blocks. So Brain Testing needs an object of that shape for them to
''' write into, and then simply DRAWS what landed there.
'''
''' THESE ARE NOT HOLLOW STUBS. MapTerrain here is a real data holder: the
''' fields carry the actual VAOs and buffers the builders produce, and this
''' app's own draw reads them. What is missing compared with nuTerra's
''' 1,200-line MapTerrain is all the BEHAVIOUR - the virtual texturing, the
''' cascade shading, the albedo bake - which is exactly the part the owner
''' asked to do without: "We don't need VT texturing. just view space clipping
''' of opject. draw the terrain as meshes."
'''
''' Field names and types are copied EXACTLY from nuTerra/Scene/MapTerrain.vb.
''' They have to be: the linked builders assign to them by name, and a type
''' that merely looks close would either fail to compile or, worse, silently
''' take a different overload.
'''
''' Added 2026-09-15 by nuTerra work, stage 2 of docs/brain_testing_plan.md.
''' </summary>
Public Class MapScene
    Public terrain As New MapTerrain
    Public sky As New MapSky
    Public mini_map As New MapMinimap

    ''' <summary>Set by the builders when the chunks are up. ChunkFunctions
    ''' tests it before touching the VAO.</summary>
    Public TERRAIN_LOADED As Boolean = False

    ''' <summary>Set when the arena declared ctf bases. Test THIS, not TEAM_1
    ''' against zero - a map can legitimately have a base at the origin.</summary>
    Public BASE_RINGS_LOADED As Boolean = False
End Class

''' <summary>The terrain's GPU state, as the builders leave it.</summary>
Public Class MapTerrain

    ''' <summary>One contiguous run of outland indices plus its world-XZ
    ''' bounds, so a block can be frustum-tested before it is issued. Copied
    ''' exactly from nuTerra's nested type.</summary>
    Public Structure OutlandBlock
        Public first_index As UInt32
        Public index_count As UInt32
        Public min_xz As Vector2
        Public max_xz As Vector2
    End Structure

    ' ---- the inland terrain: what this app actually draws ----------------
    Public all_chunks_vao As GLVertexArray
    Public vertices_buffer As GLBuffer
    Public indices_buffer As GLBuffer
    Public indirect_buffer As GLBuffer
    Public matrices As GLBuffer

    ' ---- the outland ring ------------------------------------------------
    ' Built because ChunkFunctions builds it; whether this app draws it is a
    ' separate question. The play field is the inner 1000 m and the AI never
    ' leaves it, so the ring is scenery.
    Public Const OUTLAND_GRID As Integer = 1024
    Public Const OUTLAND_WELD_BAND As Single = 45.0F
    Public Const OUTLAND_CULL_BLOCK As Integer = 64

    Public CASCADE_LEVELS As Integer = 0
    Public outland_vao As GLVertexArray
    Public outland_far_vao As GLVertexArray
    Public outland_vertices_buffer As GLBuffer
    Public outland_indices_buffer As GLBuffer
    Public outland_far_indices_buffer As GLBuffer
    Public outland_near_indirect As GLBuffer
    Public outland_far_indirect As GLBuffer
    Public outland_near_index_count As Integer
    Public outland_far_index_count As Integer
    Public outland_near_blocks() As OutlandBlock
    Public outland_far_blocks() As OutlandBlock
    Friend outland_near_cmds() As DrawElementsIndirectCommand
    Friend outland_far_cmds() As DrawElementsIndirectCommand

    ' ---- textures the builders load and this app never binds -------------
    ' Left as fields rather than removed: the builders assign them, and every
    ' one is Nothing here because TextureMgr is stubbed. "no textures."
    Public GLOBAL_AM_ID As GLTexture
    Public HOLE_MASK_ID As GLTexture
    Public OUTLAND_TILE As GLTexture
    Public OUTLAND_TILES() As GLTexture
    Public OUTLAND_TILE_CASCADE As GLTexture
    Public OUTLAND_TILE_SCALE As Single
    Public OUTLAND_TILE_SCALE_CASCADE As Single
    Public OUTLAND_NORMAL_MAP As GLTexture
    Public OUTLAND_NORMAL_CASCADE_MAP As GLTexture
    Public OUTLAND_height_MAP As GLTexture
    Public OUTLAND_height_CASCADE_MAP As GLTexture

    ''' <summary>nuTerra bakes the outland albedo into an atlas. Nothing here
    ''' samples it, so this is the one member that is genuinely a no-op.</summary>
    Public Sub bake_outland_albedo()
    End Sub

    ''' <summary>nuTerra rebuilds the virtual texture atlas here. There is no
    ''' atlas: "We don't need VT texturing."</summary>
    Public Sub RebuildVTAtlas()
    End Sub
End Class

''' <summary>Stands in for nuTerra/Scene/MapSky.vb. The builders record which
''' cube and sun textures the environment named; no sky is drawn.</summary>
Public Class MapSky
    Public CUBE_TEXTURE_PATH As String
    Public SUN_TEXTURE_PATH As String
    Public skybox_mdl As XModel
    Public texture As GLTexture
End Class

''' <summary>Stands in for nuTerra/Scene/MapMinimap.vb.</summary>
Public Class MapMinimap
    Public MINI_MAP_ID As GLTexture
    Public TEAM_1_ICON_ID As GLTexture
    Public TEAM_2_ICON_ID As GLTexture
End Class

''' <summary>Stands in for the .x model in nuTerra/ModelLoaders/modXfile.vb -
''' the skybox dome, which this app does not draw.</summary>
Public Class XModel
    ''' <summary>The skybox dome. Nothing draws a sky, so the loader is not
    ''' linked and this hands back Nothing.</summary>
    Public Shared Function load_from_file(file_ As String) As XModel
        Return Nothing
    End Function
End Class

''' <summary>Stands in for nuTerra/Particles/modParticles.vb. modGlobalVars
''' declares a list of placements whether or not anything emits.</summary>
Public Module modParticles
    Public Class PfxPlacement
    End Class
End Module

''' <summary>
''' Stands in for TCommonProperties in nuTerra/OpenGL/modOpenGL.vb - the
''' uniform block every shader reads.
'''
''' Only the fields the linked terrain and space.bin code ASSIGNS. There are
''' no shaders here reading a UBO, so this is a place for those writes to
''' land rather than a block that is uploaded. The real one is not linked
''' because its 2D helpers reach for MainFBO and two shaders, which drags
''' the framebuffer and shader systems in behind it.
''' </summary>
Public Module BrainCommonProperties
    Public CommonProperties As New TCommonProperties
End Module

Public Class TCommonProperties
    Public waterColor As Vector3
    Public waterAlpha As Single
    Public fog_tint As Vector3
    Public sunColor As Vector3
    Public ambientColorForward As Vector3
    Public map_size As Vector2
    Public blend_macro_influence As Single
    Public blend_global_threshold As Single
    Public blend_height As Single
    Public disabled_blend_height As Single
    Public Shared blend_height_authored As Single
    Public Const GAME_BLEND_HEIGHT As Single = 0.05F

    ''' <summary>nuTerra re-uploads the uniform block here. Nothing to
    ''' upload: no shader in this app reads it.</summary>
    Public Sub update()
    End Sub
End Class
