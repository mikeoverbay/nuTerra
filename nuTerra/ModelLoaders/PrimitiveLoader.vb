Imports System.IO
Imports System.Math
Imports OpenTK.Mathematics

Module PrimitiveLoader
    Public Class BinarySectionInfo
        Public location As UInt32
        Public size As UInt32
    End Class

    Public Enum ShaderTypes
        FX_PBS_ext = 1
        FX_PBS_ext_dual = 2
        FX_PBS_ext_detail = 3
        FX_PBS_tiled_atlas = 4
        FX_PBS_tiled_atlas_global = 5
        FX_PBS_glass = 6
        FX_PBS_ext_repaint = 7
        FX_lightonly_alpha = 8
        FX_unsupported = 9
        ''' <summary>shaders/std_effects/glow.fx - an unlit emissive card.
        ''' The compiled shader is diffuse, alpha test, gamma decode, then
        ''' rgb * g_tintColor * (selfIllumination + 1) * g_envLumMultipliers.x
        ''' with fog - no lighting of any kind, one render target. Shares
        ''' MaterialProps_lightonly_alpha, which already carries everything
        ''' it needs.</summary>
        FX_glow = 13
        FX_PBS_tiled = 10
        ''' <summary>shaders/custom/volumetric_effect*.fx - GFX smoke columns,
        ''' flame sheets, distortion cards. Translucent, drawn forward after
        ''' the deferred resolve. Transcribed from the game's compiled
        ''' volumetric_effect_vtx fxo (see volumetric.vert/frag).</summary>
        FX_volumetric = 11
        ''' <summary>shaders/std_effects/PBS_tiled_global.fx - big unique-
        ''' unwrap rocks/cliffs on the newer maps (Graf Zeppelin, Lost
        ''' Paradise, ...). Three height-blended detail tiles at
        ''' uv1 * g_tileUVScale plus a per-object global set at uv2: blend
        ''' mask (A = baked AO), colorTex GCM (recolours the tiles), and
        ''' globalTex GNM (global normal, B*2 = baked shadow). Transcribed
        ''' from the fxo - see FX_PBS_tiled_global_entry in model.frag.</summary>
        FX_PBS_tiled_global = 12
    End Enum

    Structure MaterialProps_PBS_ext
        Public diffuseMap As String
        Public normalMap As String
        Public metallicGlossMap As String
        Public alphaReference As Integer
        Public alphaTestEnable As Boolean
        Public doubleSided As Boolean
        Public g_useNormalPackDXT1 As Boolean
        'Public g_useTintColor As Boolean
        Public g_colorTint As Vector4
        Public g_enableAO As Boolean
    End Structure

    Structure MaterialProps_volumetric
        Public diffuseMap As String
        Public distortionMap As String
        Public TintlColor As Vector4
        Public diffuseUVSpeedAlphaOffset As Vector4
        Public distortion_UV_Speed_Amount As Vector4
        Public lightMultipliers As Vector4
        Public selfIllumLight As Vector4
        Public FreshnelColor As Vector4          ' WG's own spelling
        Public alphaFadeAmountFresnel As Vector4
        Public alphaAdditiveEnable As Boolean
        Public doubleSided As Boolean
        Public enableLighting As Boolean
        ' Selects the compiled shader variant (fxo has both): True = fresnel
        ' thinning in the VS plus the (x-1)*gain alpha remap in the PS;
        ' False = no fresnel and plain alpha = texA * vertA * fade.
        Public alphaFreshnelEnable As Boolean
        ' Distance fade-in window (register defaults 0.01 / 1.0 = always on
        ' past a metre). Backdrop sheets author real ranges - SmokeBotton
        ' 150..400 - so they exist only at distance.
        Public fadeMinDistance As Single
        Public fadeMaxDistance As Single
        ''' <summary>
        ''' Soft-particle fade distance. cb0[81].x, used by BOTH compiled pixel
        ''' variants - it is only [unused] in the vertex shader, which is what
        ''' made it look dead.
        ''' </summary>
        Public softFactor As Single
        ' D3DBLEND dest factor. 2 (ONE) composites additively even when
        ' alphaAdditiveEnable is not set.
        Public destBlend As Integer
    End Structure

    Structure MaterialProps_PBS_ext_dual
        Public diffuseMap As String
        Public diffuseMap2 As String
        Public normalMap As String
        Public metallicGlossMap As String
        Public alphaReference As Integer
        Public alphaTestEnable As Boolean
        Public doubleSided As Boolean
        Public g_useNormalPackDXT1 As Boolean
        'Public g_useTintColor As Boolean
        Public g_colorTint As Vector4
    End Structure

    Structure MaterialProps_PBS_ext_detail
        Public diffuseMap As String
        Public normalMap As String
        Public metallicGlossMap As String
        Public alphaReference As Integer
        Public alphaTestEnable As Boolean
        Public doubleSided As Boolean
        Public g_detailMap As String
        Public g_useNormalPackDXT1 As Boolean
        Public g_enableAO As Boolean
        'Public g_useTintColor As Boolean
        Public g_colorTint As Vector4
        Public g_detailInfluences As Vector4
        Public g_detailRejectTiling As Vector4
    End Structure

    Structure MaterialProps_PBS_tiled_atlas
        Public atlasAlbedoHeight As String
        Public atlasBlend As String
        Public atlasNormalGlossSpec As String
        Public atlasMetallicAO As String
        Public dirtMap As String
        Public globalTex As String
        Public dirtParams As Vector4
        Public dirtColor As Vector4
        Public g_atlasSizes As Vector4
        Public g_atlasIndexes As Vector4
        Public g_tile0Tint As Vector4
        Public g_tile1Tint As Vector4
        Public g_tile2Tint As Vector4
        Public g_tileUVScale As Vector4
        Public alphaReference As Integer
        Public alphaTestEnable As Boolean
    End Structure

    Structure MaterialProps_PBS_atlas_global
        Public atlasAlbedoHeight As String
        Public atlasBlend As String
        Public atlasNormalGlossSpec As String
        Public atlasMetallicAO As String
        Public dirtMap As String
        Public globalTex As String
        Public dirtParams As Vector4
        Public dirtColor As Vector4
        Public g_atlasSizes As Vector4
        Public g_atlasIndexes As Vector4
        Public g_tile0Tint As Vector4
        Public g_tile1Tint As Vector4
        Public g_tile2Tint As Vector4
        Public g_tileUVScale As Vector4
        Public alphaReference As Integer
        Public alphaTestEnable As Boolean
    End Structure

    ''' <summary>
    ''' shaders/std_effects/PBS_tiled.fx
    ''' Replaced PBS_tiled_atlas.fx: the three tile sets are now discrete
    ''' textures instead of layers indexed into a shared atlas, so there is
    ''' no g_atlasIndexes / g_tileUVScale any more.
    ''' </summary>
    Structure MaterialProps_PBS_tiled
        Public albedoHeightTile0 As String
        Public normalGlossSpecTile0 As String
        Public metallicAOTile0 As String
        Public albedoHeightTile1 As String
        Public normalGlossSpecTile1 As String
        Public metallicAOTile1 As String
        Public albedoHeightTile2 As String
        Public normalGlossSpecTile2 As String
        Public metallicAOTile2 As String
        Public blendMask As String
        Public dirtMap As String
        Public colorTex As String
        Public g_tile0Tint As Vector4
        Public g_tile1Tint As Vector4
        Public g_tile2Tint As Vector4
        Public g_dirtColor As Vector4
        Public g_dirtColorParams As Vector4
        Public g_fakeShadowsAndDetailParams As Vector4
        Public alphaReference As Integer
        Public alphaTestEnable As Boolean
        Public doubleSided As Boolean
    End Structure

    ''' <summary>
    ''' shaders/std_effects/PBS_tiled_global.fx
    ''' PBS_tiled plus a per-object "global" texture set. Materials also
    ''' author normalGlossSpec/metallicAO tiles, but the game's own compiled
    ''' techniques never sample them for this fx (the normal comes entirely
    ''' from the GNM), so they are parsed as known and not loaded.
    ''' </summary>
    Structure MaterialProps_PBS_tiled_global
        Public albedoHeightTile0 As String
        Public albedoHeightTile1 As String
        Public albedoHeightTile2 As String
        Public blendMask As String
        Public dirtMap As String
        Public colorTex As String
        Public globalTex As String
        Public g_tileUVScale As Vector4
        Public g_tintParams As Vector4
        Public g_dirtColorParams As Vector4
        Public g_dirtColor As Vector4
        Public doubleSided As Boolean
    End Structure

    Structure MaterialProps_PBS_glass
        Public dirtAlbedoMap As String
        Public normalMap As String
        Public glassMap As String
        Public alphaTestEnable As Boolean
        Public alphaReference As Integer
        Public texAddressMode As UInteger
        Public g_filterColor As Vector4
    End Structure

    Structure MaterialProps_PBS_ext_repaint
        Public diffuseMap As String
        Public normalMap As String
        Public metallicGlossMap As String
        Public g_baseColor As Vector4
        Public g_repaintColor As Vector4
        Public alphaReference As Integer
        Public alphaTestEnable As Boolean
        Public g_enableAO As Boolean
        Public g_enableTerrainBlending As Boolean
        Public g_aging As Boolean
        Public doubleSided As Boolean
        Public selfIllumination As Integer
        Public dirtAlbedoMap As String
        Public glassMap As String
        Public g_detailInfluences As Vector4
        Public g_detailMap As String
        Public g_detailRejectTiling As Vector4
    End Structure

    Structure MaterialProps_lightonly_alpha
        Public diffuseMap As String
        Public alphaTestEnable As Boolean
        Public alphaReference As Single
        Public doubleSided As Boolean
        ''' <summary>glow.fx only. The shader multiplies by (this + 1), so 0
        ''' is the neutral default and every other family leaves it there.</summary>
        Public selfIllumination As Single
    End Structure

    Structure Material
        Public id As UInt32
        Public shader_type As ShaderTypes
        Public props As Object
    End Structure

    Class PrimitiveGroup
        Public startIndex As Integer
        Public nPrimitives As Integer
        Public startVertex As Integer
        Public nVertices As Integer
        Public material_id As Integer

        Public no_draw As Boolean
    End Class

    Public Function get_primitive(ByRef mdl As base_model_holder_) As Boolean
        If mdl.junk Then
            Return True
        End If

        Dim filename = mdl.render_sets(0).prims_name.Replace(".primitives", ".primitives_processed")
        filename = filename.Substring(0, filename.LastIndexOf("/"c)) ' remove "/indices" at the end

        ' search everywhere!

        Dim entry = ResMgr.Lookup(filename)
        If entry Is Nothing Then
            MsgBox("Can't find " + filename, MsgBoxStyle.Exclamation, "shit!")
            Return False
        End If

        Dim ms As New MemoryStream
        entry.Extract(ms)

        Try
            load_primitive(ms, mdl)
            Return True
        Catch ex As Exception
            MsgBox("Can't load " + filename, MsgBoxStyle.Exclamation, "shit!")
            Return False
        End Try
    End Function

    Public Sub load_primitive(ms As MemoryStream,
                              ByRef mdl As base_model_holder_)
        ms.Position = 0
        Dim br As New BinaryReader(ms, System.Text.Encoding.ASCII)

        'get table start position
        br.BaseStream.Position = br.BaseStream.Length - 4
        Dim table_start = br.ReadUInt32

        'point at start of table
        br.BaseStream.Position = br.BaseStream.Length - 4 - table_start

        Dim binSectionOffset = 4
        Dim binSections As New Dictionary(Of String, BinarySectionInfo)

        While br.BaseStream.Position < br.BaseStream.Length - 4
            Dim section As New BinarySectionInfo With {
                .size = br.ReadUInt32,
                .location = binSectionOffset
            }

            binSectionOffset += section.size

            ' Make binary section offset align
            If section.size Mod 4 > 0 Then
                binSectionOffset += 4 - section.size Mod 4
            End If

            ' Skip 16 bytes of unused junk
            br.BaseStream.Position += 16

            ' Get section names length
            Dim sec_name_len As UInt32 = br.ReadUInt32

            ' Get sections name
            Dim sec_name = br.ReadChars(sec_name_len)
            ' Skip pad characters
            Dim l = sec_name_len Mod 4
            If l > 0 Then
                br.BaseStream.Position += 4 - l
            End If

            binSections(sec_name) = section
        End While


        For Each renderSet In mdl.render_sets
            Dim vertsSectionName = renderSet.verts_name.Substring(renderSet.verts_name.LastIndexOf("/"c) + 1)
            Dim primsSectionName = renderSet.prims_name.Substring(renderSet.prims_name.LastIndexOf("/"c) + 1)
            load_primitives_indices(br, renderSet, binSections(primsSectionName))
            load_primitives_vertices(br, renderSet, binSections(vertsSectionName))
            Dim uv2SectionName = If(vertsSectionName.Contains("."), vertsSectionName.Split(".")(0) + ".uv2", "uv2")
            If binSections.ContainsKey(uv2SectionName) Then
                load_primitives_uv2(br, renderSet, binSections(uv2SectionName))
            End If
            Dim colourSectionName = If(vertsSectionName.Contains("."), vertsSectionName.Split(".")(0) + ".colour", "colour")
            If binSections.ContainsKey(colourSectionName) Then
                load_primitives_colour(br, renderSet, binSections(colourSectionName))
            End If
        Next
    End Sub

    Public Sub load_primitives_indices(br As BinaryReader,
                                       ByRef renderSet As RenderSetEntry,
                                       ByRef sectionInfo As BinarySectionInfo)
        br.BaseStream.Position = sectionInfo.location

        ' "list" = UInt16 pointers
        ' "list32" = UInt32 pointers

        Dim triTypeName As New String(br.ReadChars(64))
        triTypeName = triTypeName.Remove(triTypeName.IndexOf(vbNullChar, System.StringComparison.Ordinal))

        Dim indexSize = If(triTypeName = "list32", 4, 2)

        Dim numIndices = br.ReadUInt32
        Dim numPrimGroups = br.ReadUInt32

        ' save current stream position
        Dim savedPos = br.BaseStream.Position

        ' The component table is at the end of the indicies list.
        br.BaseStream.Position += numIndices * indexSize

        ' read the tables
        For z = 0 To numPrimGroups - 1
            If Not renderSet.primitiveGroups.ContainsKey(z) Then
                renderSet.primitiveGroups(z) = New PrimitiveGroup
                renderSet.primitiveGroups(z).no_draw = True
            End If
            With renderSet.primitiveGroups(z)
                .startIndex = br.ReadInt32
                .nPrimitives = br.ReadInt32
                .startVertex = br.ReadInt32
                .nVertices = br.ReadInt32
            End With
        Next

        ' restore position
        br.BaseStream.Position = savedPos

        'We flip the winding order because of directX to Opengl 
        ReDim renderSet.buffers.index_buffer32((numIndices / 3) - 1)
        If indexSize = 2 Then
            For k = 0 To renderSet.buffers.index_buffer32.Length - 1
                With renderSet.buffers.index_buffer32(k)
                    .y = br.ReadUInt16
                    .x = br.ReadUInt16
                    .z = br.ReadUInt16
                End With
            Next
        Else
            For k = 0 To renderSet.buffers.index_buffer32.Length - 1
                With renderSet.buffers.index_buffer32(k)
                    .y = br.ReadUInt32
                    .x = br.ReadUInt32
                    .z = br.ReadUInt32
                End With
            Next
        End If
    End Sub


    Public Sub load_primitives_vertices(br As BinaryReader,
                                        ByRef renderSet As RenderSetEntry,
                                        ByRef sectionInfo As BinarySectionInfo)
        br.BaseStream.Position = sectionInfo.location

        Dim vertTypeName As New String(br.ReadChars(64))
        vertTypeName = vertTypeName.Remove(vertTypeName.IndexOf(vbNullChar, System.StringComparison.Ordinal))

        '-------------------------------
        Dim BPVT_mode As Boolean = False
        Dim realNormals As Boolean = False
        Dim hasIdx As Boolean = False
        Dim stride As Integer = 0
        renderSet.has_tangent = False

        ' get stride and flags of each vertex element
        Select Case vertTypeName
            Case "xyznuv"
                stride = 32
                realNormals = True
                renderSet.element_count = 4
                renderSet.has_tangent = False

            Case "BPVTxyznuv"
                BPVT_mode = True
                stride = 24
                realNormals = False
                renderSet.element_count = 4
                renderSet.has_tangent = False

            Case "xyznuviiiwwtb"
                stride = 37
                renderSet.element_count = 5
                renderSet.has_tangent = True
                hasIdx = True

            Case "BPVTxyznuviiiww"
                BPVT_mode = True
                stride = 32
                renderSet.element_count = 4
                hasIdx = True

            Case "BPVTxyznuviiiwwtb"
                BPVT_mode = True
                stride = 40
                renderSet.element_count = 5
                renderSet.has_tangent = True
                hasIdx = True

            Case "xyznuvtb"
                stride = 32
                renderSet.element_count = 5
                renderSet.has_tangent = True

            Case "BPVTxyznuvtb"
                BPVT_mode = True
                stride = 32
                renderSet.element_count = 5
                renderSet.has_tangent = True

            Case Else
                Debug.Assert(False)

        End Select

        If BPVT_mode Then
            br.BaseStream.Position += 68 ' move to where count is located
        End If


        renderSet.numVertices = br.ReadUInt32 ' read total count of vertcies
        Debug.Assert(renderSet.numVertices > 2)

        ' should be in same offset in both buffers.
        '---------------------------
        ReDim renderSet.buffers.vertexBuffer(renderSet.numVertices - 1)

        Dim running As Integer = 0 'Continuous accumulator pointer in to the buffers

        For Each primGroup In renderSet.primitiveGroups.Values
            For z = primGroup.startVertex To primGroup.startVertex + primGroup.nVertices - 1
                '-----------------------------------------------------------------------
                'We have to flip the sign of X on all vertex values because of DirectX to OpenGL

                '-----------------------------------------------------------------------
                'vertex
                With renderSet.buffers.vertexBuffer(running)
                    .pos.X = -br.ReadSingle
                    .pos.Y = br.ReadSingle
                    .pos.Z = br.ReadSingle

                    round_signed_to(.pos.X, 3)
                    round_signed_to(.pos.Y, 3)
                    round_signed_to(.pos.Z, 3)

                    If realNormals Then
                        .normal.X = -br.ReadSingle
                        .normal.Y = br.ReadSingle
                        .normal.Z = br.ReadSingle
                    Else
                        Dim v3 = unpackNormal_8_8_8(br.ReadUInt32) ' unpack normals
                        .normal.X = -v3.X
                        .normal.Y = v3.Y
                        .normal.Z = v3.Z
                    End If
                    .uv.X = br.ReadSingle
                    .uv.Y = br.ReadSingle

                    '-----------------------------------------------------------------------
                    'if this vertex has index junk, skip it.
                    'no tangent and bitangent on BPVTxyznuviiiww type vertex
                    If hasIdx Then
                        br.BaseStream.Position += 8
                    End If

                    If renderSet.has_tangent Then
                        'tangents
                        Dim v3 = unpackNormal_8_8_8(br.ReadUInt32)
                        .tangent.X = -v3.X
                        .tangent.Y = v3.Y
                        .tangent.Z = v3.Z
                        v3 = unpackNormal_8_8_8(br.ReadUInt32)
                        .binormal.X = -v3.X
                        .binormal.Y = v3.Y
                        .binormal.Z = v3.Z
                    End If

                    running += 1

                End With
            Next
        Next

        ' Skinned formats (the iiiww families - boats, anything with an
        ' animations folder) come off disk with the OPPOSITE winding to the
        ' static exports, so the DX-to-GL triangle flip the index loader
        ' applies to everything is one flip too many for them and they render
        ' inside out. The indices are loaded before the vertex format is known,
        ' which is why the correction lives here: put their winding back.
        If hasIdx Then
            For k = 0 To renderSet.buffers.index_buffer32.Length - 1
                With renderSet.buffers.index_buffer32(k)
                    Dim tmp = .x
                    .x = .y
                    .y = tmp
                End With
            Next
        End If
    End Sub
    Public Sub round_signed_to(ByRef n As Single, ByRef places As Integer)
        Dim t As Single = Truncate(n)
        Dim r As Integer = (n - t) * (10 ^ places)
        Dim r2 As Single = r / (10 ^ places)
        n = t + r2
    End Sub

    Public Sub load_primitives_uv2(br As BinaryReader,
                                   ByRef renderSet As RenderSetEntry,
                                   ByRef sectionInfo As BinarySectionInfo)
        br.BaseStream.Position = sectionInfo.location

        Dim uv2_subname As New String(br.ReadChars(64))
        uv2_subname = uv2_subname.Remove(uv2_subname.IndexOf(vbNullChar, System.StringComparison.Ordinal))
        Debug.Assert(uv2_subname.StartsWith("BPVS"))

        Dim unused = br.ReadUInt32()
        Debug.Assert(unused = 0)

        Dim uv2_format As New String(br.ReadChars(64))
        uv2_format = uv2_format.Remove(uv2_format.IndexOf(vbNullChar, System.StringComparison.Ordinal))
        Debug.Assert(uv2_format = "set3/uv2pc")

        Dim uv2_count = br.ReadUInt32()
        Debug.Assert(uv2_count = renderSet.buffers.vertexBuffer.Length)

        ReDim renderSet.buffers.uv2(uv2_count - 1)
        For i = 0 To uv2_count - 1
            renderSet.buffers.uv2(i).X = br.ReadSingle
            renderSet.buffers.uv2(i).Y = br.ReadSingle
        Next
    End Sub

    ''' <summary>
    ''' The "colour" stream section: BPVScolour header, format string "colour",
    ''' count, then RGBA8 per vertex. GFX volumetric meshes carry their edge
    ''' fade in the alpha - without it every smoke sheet is a hard card.
    ''' </summary>
    Public Sub load_primitives_colour(br As BinaryReader,
                                      ByRef renderSet As RenderSetEntry,
                                      ByRef sectionInfo As BinarySectionInfo)
        br.BaseStream.Position = sectionInfo.location

        Dim subname As New String(br.ReadChars(64))
        subname = subname.Remove(subname.IndexOf(vbNullChar, System.StringComparison.Ordinal))
        Debug.Assert(subname.StartsWith("BPVS"))

        Dim unused = br.ReadUInt32()
        Debug.Assert(unused = 0)

        Dim colour_format As New String(br.ReadChars(64))
        colour_format = colour_format.Remove(colour_format.IndexOf(vbNullChar, System.StringComparison.Ordinal))
        Debug.Assert(colour_format = "colour")

        Dim colour_count = br.ReadUInt32()
        Debug.Assert(colour_count = renderSet.buffers.vertexBuffer.Length)

        ReDim renderSet.buffers.colour(colour_count - 1)
        For i = 0 To colour_count - 1
            renderSet.buffers.colour(i) = br.ReadUInt32
        Next
    End Sub

    Public Function unpackNormal_8_8_8(packed As UInt32) As Vector3
        Dim p As New Vector3 With {
            .X = CLng(packed) And &HFF Xor 127,
            .Y = CLng(packed >> 8) And &HFF Xor 127,
            .Z = CLng(packed >> 16) And &HFF Xor 127
        }
        If p.X > 127 Then p.X -= 256
        If p.Y > 127 Then p.Y -= 256
        If p.Z > 127 Then p.Z -= 256
        p.NormalizeFast()
        Return -p
    End Function

    Public Function unpackNormal(ByVal packed As UInt32) As Vector3
        Dim pkz, pky, pkx As Int32
        pkz = packed And &HFFC00000
        pky = packed And &H4FF800
        pkx = packed And &H7FF

        Dim z As Int32 = pkz >> 22
        Dim y As Int32 = (pky << 10L) >> 21
        Dim x As Int32 = (pkx << 21L) >> 21
        Dim p As New Vector3
        p.X = CSng(x) / 1023.0!
        p.Y = CSng(y) / 1023.0!
        p.Z = CSng(z) / 511.0!
        Dim len As Single = Sqrt((p.X ^ 2) + (p.Y ^ 2) + (p.Z ^ 2))

        'avoid division by 0
        If len = 0.0F Then len = 1.0F

        'reduce to unit size (normalize)
        p.X = (p.X / len)
        p.Y = (p.Y / len)
        p.Z = (p.Z / len)
        Return p
    End Function

End Module
