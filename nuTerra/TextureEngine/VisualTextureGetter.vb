Imports System.IO

Module VisualTextureGetter

    Public Function Not_Crushed(ByRef ident As String, fn As String) As Boolean
        If fn.Contains("bbox/building_wall1") Then
            Return True
        End If

        If ident.Contains("s_nd") Then ' stone no_distruction
            Return False
        End If
        If ident.Contains("n_met") Then ' Normal Metal
            Return False
        End If
        If ident.Contains("n_woo") Then ' Normal wood
            Return False
        End If

        If ident.Contains("n_ston") Then ' Normal stone
            Return False
        End If
        '======================================================
        If ident.Contains("s_") Then 's_wall, s_ramp s_ ??
            Return True
        End If

        If ident.Contains("d_wo") Then ' damaged wood
            Return True
        End If
        If ident.Contains("d_met") Then ' damaged metal
            Return True
        End If
        If ident.Contains("d_sto") Then ' damaged stone
            Return True
        End If
        'Last chance to figure out if this is to be drawn
        Return can_this_be_broken(ident)

    End Function


    Public Sub get_textures_and_settings(ByRef m As entries_, ByVal p_group As Integer, ByVal cur_mdl As Integer, fn As String)
        ' .entries , p_group id, cur_pos
        Dim is_atlas As Boolean = True
        'First, we set some defaults. They need to be there for the shader.
        preset_visual_Data(m)
        Dim thestring = visual_sections(cur_mdl).p_group(p_group)

        ' has to be done to stop grabbing DiffuseMap2 when looking for DiffuseMap
        thestring = thestring.Replace("diffuseMap2", "difffuseMap2")

        Dim in_pos As Integer
        'The order of seaching the string speeds up loading.
        'If we find a diffuseMap in the string, than it is NOT an
        'Atlas texture and we set the flag to false and skip searching
        'for atlas related items.

        'The idnentifier is always first in the visual chunk.
        'Becasue we are NOT drawing useless broken junk,
        'we need to not waste time looking for textures 
        'for a component we will never be drawn or adding the 
        'primitive count to TOTAL_TRIANGLES_DRAWN_MODEL.
        '
        'If idenetifier tells us this is not drawn, return.
        'We don't care about textures!
        in_pos = InStr(1, thestring, "<identifier>")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<identifier>") + "<identifier>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</identifier>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            m.identifier = newS
            m.draw = Not_Crushed(newS, fn)
            If m.draw Then
                Return ' this is NOT a drawn component.
            End If
        End If
        '
        in_pos = InStr(1, thestring, "fx")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<fx>") + "<fx>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</fx>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            m.FX_shader = newS
        End If
        '
        '===========================================================
        ' TEXTURE NAMES ============================================
        '===========================================================
        in_pos = InStr(1, thestring, "diffuseMap")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<Texture>") + "<Texture>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</Texture>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            m.diffuseMap = newS
            m.diffuseMap_id = find_and_load_texture_from_pkgs(newS)
            thestring = thestring.Replace("difffuseMap2", "diffuseMap2") 'now we can put it back to norma
            is_atlas = False
        End If
        in_pos = InStr(1, thestring, "normalMap")
        '
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<Texture>") + "<Texture>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</Texture>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            m.normalMap = newS
            m.normalMap_id = find_and_load_texture_from_pkgs(newS)
        End If
        '
        in_pos = InStr(1, thestring, "diffuseMap2")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<Texture>") + "<Texture>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</Texture>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            m.diffuseMap2 = newS
            m.has_uv2 = 1 'This should already be set but lets do it again.
            m.diffuseMap2_id = find_and_load_texture_from_pkgs(newS)
        End If
        '
        in_pos = InStr(1, thestring, "metallicGlossMap")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<Texture>") + "<Texture>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</Texture>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            m.metallicGlossMap = newS
            m.metallicGlossMap_id = find_and_load_texture_from_pkgs(newS)
        End If
        '
        If is_atlas Then

            in_pos = InStr(1, thestring, "atlasBlend")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Texture>") + "<Texture>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Texture>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                m.atlasBlend = newS
                m.atlasBlend_id = find_and_load_texture_from_pkgs(newS)
            End If
            '
            in_pos = InStr(1, thestring, "atlasMetallicAO")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Texture>") + "<Texture>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Texture>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                m.atlasMetallicAO = newS
                m.atlasMetallicAO_id = find_and_load_texture_from_pkgs(newS)
            End If
            '
            in_pos = InStr(1, thestring, "atlasNormalGlossSpec")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Texture>") + "<Texture>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Texture>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                m.atlasNormalGlossSpec = newS
                m.atlasNormalGlossSpec_id = find_and_load_texture_from_pkgs(newS)
            End If
            '
            in_pos = InStr(1, thestring, "atlasAlbedoHeight")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Texture>") + "<Texture>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Texture>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                m.atlasAlbedoHeight = newS
                m.atlasAlbedoHeight_id = find_and_load_texture_from_pkgs(newS)
            End If
            '
            in_pos = InStr(1, thestring, "dirtMap")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Texture>") + "<Texture>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Texture>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                m.dirtMap = newS
                m.dirtMap_id = find_and_load_texture_from_pkgs(newS)
            End If
            '
            in_pos = InStr(1, thestring, "globalTex")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Texture>") + "<Texture>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Texture>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                m.globalTex = newS
                m.globalTex_id = find_and_load_texture_from_pkgs(newS)
            End If
        End If 'is_atlas
        '===========================================================
        ' BOOLEANS =================================================
        '===========================================================
        in_pos = InStr(1, thestring, "doubleSided")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<Bool>") + "<Bool>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</Bool>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            If newS = "true" Then
                m.doubleSided = 1
            Else
                m.doubleSided = 0
            End If
        End If
        '
        in_pos = InStr(1, thestring, "alphaTestEnable")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<Bool>") + "<Bool>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</Bool>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            If newS = "true" Then
                m.alphaEnable = 1
            Else
                m.alphaEnable = 0
            End If
        End If
        '
        in_pos = InStr(1, thestring, "alphaEnable")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<Bool>") + "<Bool>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</Bool>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            If newS = "true" Then
                m.alphaEnable = 1
            Else
                m.alphaEnable = 0
            End If
        End If
        '
        in_pos = InStr(1, thestring, "dynamicobject")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<Bool>") + "<Bool>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</Bool>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            If newS = "true" Then
                m.dynamicobject = 1
            Else
                m.dynamicobject = 0
            End If
        End If
        '
        in_pos = InStr(1, thestring, "g_enableAO")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<Bool>") + "<Bool>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</Bool>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            If newS = "true" Then
                m.g_enableAO = 1
            Else
                m.g_enableAO = 0
            End If
        End If
        '
        'If this is a one, the model uses the RGB tangent normal maps.
        'If this is zero, It uses the compressed AG (alpha in red) maps.
        in_pos = InStr(1, thestring, "g_useNormalPackDXT1")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<Bool>") + "<Bool>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</Bool>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            If newS = "true" Then
                m.g_useNormalPackDXT1 = 1
            Else
                m.g_useNormalPackDXT1 = 0
            End If
        End If
        '===========================================================
        ' VALUES ===================================================
        '===========================================================
        'integers -------------------------------
        in_pos = InStr(1, thestring, "alphaReference")
        If in_pos > 0 Then
            Dim tex1_pos = InStr(in_pos, thestring, "<Int>") + "<Int>".Length
            Dim tex1_Epos = InStr(in_pos, thestring, "</Int>")
            Dim newS As String = ""
            newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
            m.alphaReference = CInt(newS)
        End If
        '
        If is_atlas Then

            in_pos = InStr(1, thestring, "TexAddressMode")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Int>") + "<Int>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Int>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                m.TexAddressMode = CInt(newS)
            End If
            '
            in_pos = InStr(1, thestring, "g_vertexColorMode")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Int>") + "<Int>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Int>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                m.g_vertexColorMode = CInt(newS)
            End If
            'Vector4 -------------------------------
            in_pos = InStr(1, thestring, "g_tintColor")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Vector4>") + "<Vector4>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Vector4>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                Dim ta = newS.Split(" ")
                m.g_tintColor.X = Convert.ToSingle(ta(0))
                m.g_tintColor.Y = Convert.ToSingle(ta(1))
                m.g_tintColor.Z = Convert.ToSingle(ta(2))
                m.g_tintColor.W = Convert.ToSingle(ta(3))
            End If
            '
            in_pos = InStr(1, thestring, "g_tile0Tint")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Vector4>") + "<Vector4>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Vector4>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                Dim ta = newS.Split(" ")
                m.g_tile0Tint.X = Convert.ToSingle(ta(0))
                m.g_tile0Tint.Y = Convert.ToSingle(ta(1))
                m.g_tile0Tint.Z = Convert.ToSingle(ta(2))
                m.g_tile0Tint.W = Convert.ToSingle(ta(3))
            End If
            '
            in_pos = InStr(1, thestring, "g_tile1Tint")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Vector4>") + "<Vector4>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Vector4>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                Dim ta = newS.Split(" ")
                m.g_tile1Tint.X = Convert.ToSingle(ta(0))
                m.g_tile1Tint.Y = Convert.ToSingle(ta(1))
                m.g_tile1Tint.Z = Convert.ToSingle(ta(2))
                m.g_tile1Tint.W = Convert.ToSingle(ta(3))
            End If
            '
            in_pos = InStr(1, thestring, "g_tile2Tint")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Vector4>") + "<Vector4>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Vector4>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                Dim ta = newS.Split(" ")
                m.g_tile2Tint.X = Convert.ToSingle(ta(0))
                m.g_tile2Tint.Y = Convert.ToSingle(ta(1))
                m.g_tile2Tint.Z = Convert.ToSingle(ta(2))
                m.g_tile2Tint.W = Convert.ToSingle(ta(3))
            End If
            '
            in_pos = InStr(1, thestring, "g_dirtParams")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Vector4>") + "<Vector4>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Vector4>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                Dim ta = newS.Split(" ")
                m.g_dirtParams.X = Convert.ToSingle(ta(0))
                m.g_dirtParams.Y = Convert.ToSingle(ta(1))
                m.g_dirtParams.Z = Convert.ToSingle(ta(2))
                m.g_dirtParams.W = Convert.ToSingle(ta(3))
            End If
            '
            in_pos = InStr(1, thestring, "g_dirtColor")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Vector4>") + "<Vector4>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Vector4>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                Dim ta = newS.Split(" ")
                m.g_dirtColor.X = Convert.ToSingle(ta(0))
                m.g_dirtColor.Y = Convert.ToSingle(ta(1))
                m.g_dirtColor.Z = Convert.ToSingle(ta(2))
                m.g_dirtColor.W = Convert.ToSingle(ta(3))
            End If
            '
            in_pos = InStr(1, thestring, "g_atlasSizes")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Vector4>") + "<Vector4>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Vector4>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                Dim ta = newS.Split(" ")
                m.g_atlasSizes.X = Convert.ToSingle(ta(0))
                m.g_atlasSizes.Y = Convert.ToSingle(ta(1))
                m.g_atlasSizes.Z = Convert.ToSingle(ta(2))
                m.g_atlasSizes.W = Convert.ToSingle(ta(3))
            End If
            '
            in_pos = InStr(1, thestring, "g_atlasIndexes")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Vector4>") + "<Vector4>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Vector4>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                Dim ta = newS.Split(" ")
                m.g_atlasIndexes.X = Convert.ToSingle(ta(0))
                m.g_atlasIndexes.Y = Convert.ToSingle(ta(1))
                m.g_atlasIndexes.Z = Convert.ToSingle(ta(2))
                m.g_atlasIndexes.W = Convert.ToSingle(ta(3))
            End If
            '
            in_pos = InStr(1, thestring, "g_vertexAnimationParams")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Vector4>") + "<Vector4>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Vector4>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                Dim ta = newS.Split(" ")
                m.g_vertexAnimationParams.X = Convert.ToSingle(ta(0))
                m.g_vertexAnimationParams.Y = Convert.ToSingle(ta(1))
                m.g_vertexAnimationParams.Z = Convert.ToSingle(ta(2))
                m.g_vertexAnimationParams.W = Convert.ToSingle(ta(3))
            End If
            '
            in_pos = InStr(1, thestring, "g_fakeShadowsParams")
            If in_pos > 0 Then
                Dim tex1_pos = InStr(in_pos, thestring, "<Vector4>") + "<Vector4>".Length
                Dim tex1_Epos = InStr(in_pos, thestring, "</Vector4>")
                Dim newS As String = ""
                newS = Mid(thestring, tex1_pos, tex1_Epos - tex1_pos).Replace("/", "\")
                Dim ta = newS.Split(" ")
                m.g_fakeShadowsParams.X = Convert.ToSingle(ta(0))
                m.g_fakeShadowsParams.Y = Convert.ToSingle(ta(1))
                m.g_fakeShadowsParams.Z = Convert.ToSingle(ta(2))
                m.g_fakeShadowsParams.W = Convert.ToSingle(ta(3))
            End If
        End If 'is_atlas

    End Sub
    Private Sub preset_visual_Data(ByRef m As entries_)

        m.g_tile0Tint.X = 1.0!
        m.g_tile0Tint.Y = 1.0!
        m.g_tile0Tint.Z = 1.0!
        m.g_tile0Tint.W = 1.0!

        m.g_tile1Tint.X = 1.0!
        m.g_tile1Tint.Y = 1.0!
        m.g_tile1Tint.Z = 1.0!
        m.g_tile1Tint.W = 1.0!

        m.g_tile2Tint.X = 1.0!
        m.g_tile2Tint.Y = 1.0!
        m.g_tile2Tint.Z = 1.0!
        m.g_tile2Tint.W = 1.0!

        m.g_dirtColor.X = 1.0!
        m.g_dirtColor.Y = 1.0!
        m.g_dirtColor.Z = 1.0!
        m.g_dirtColor.W = 1.0!

        m.g_dirtParams.X = 1.0!
        m.g_dirtParams.Y = 1.0!
        m.g_dirtParams.Z = 1.0!
        m.g_dirtParams.W = 0.0!

    End Sub
End Module
