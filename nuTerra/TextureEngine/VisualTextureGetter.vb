Module VisualTextureGetter

    Dim model_bump_loaded As Boolean


    Public Function this_is_uncrushed(ByRef modID As UInt32, ByRef peice As Integer) As Boolean
        'redoing this.. old way sucks!!!
        Dim primStart As Integer = InStr(TheXML_String, "tiveGroup>" + peice.ToString)

        If primStart = 0 Then
            Return False
        End If
        Dim di_ As Integer = InStr(primStart, TheXML_String, "<identifier>") + "<identifier>".Length
        Dim di_end As Integer = InStr(di_, TheXML_String, "</identifier>")
        Dim material As String = Mid(TheXML_String, di_, di_end - di_)
        Dim fn = Models.Model_list(modID).ToLower
        'If InStr(fn, "env205_Boats02") > 0 Then
        '    Stop
        'End If
        fn = fn.Replace("lod1", "lod0")
        fn = fn.Replace("lod2", "lod0")
        fn = fn.Replace("lod3", "lod0")
        If fn.Contains("mle040_") And peice = 1 Then
            Return False
        End If
        If fn.Contains("mle008_") And peice = 2 Then
            Return False
        End If
        If fn.Contains("env053_") And peice = 0 Then
            Return False
        End If
        'If fn.Contains("bldAM_008") Then
        '    Stop
        'End If
        If material.Contains("n_stone") Then
            'Debug.WriteLine(material + " " + peice.ToString("00"))
            'Return True
        End If
        'If material.Contains("n_wood") Then
        '    Return False
        'End If
        'If material.Contains("n_metal") Then
        '    Return False
        'End If
        'If material.Contains("partN_") Then
        '    Return False
        'End If

        If material.Contains("s_wall") Then
            Return True
        End If
        If material.Contains("s_ramp") Then
            Return True
        End If
        If material.Contains("s_n") Then
            Return False
        End If
        If material.Contains("d_wo") Then
            Return True
        End If
        If material.Contains("d_me") Then
            Return True
        End If
        If material.Contains("d_sto") Then
            Return True
        End If
        fn = fn.Replace("_processed", "")

        Return can_this_be_broken(material)

        Return False
    End Function


    Public Function get_textures_and_names(ByVal mod_id As Integer, ByVal currentP As Integer, ByVal primNum As Integer, ByRef has_uv2 As Boolean) As Boolean
        'the old method sucks so.. im redoing it!
        'If map = 63 And mod_id = 18 Then
        '	Stop
        'End If
        Models.models(mod_id).componets(currentP).alpha_only = False
        ' Models.models(mod_id).componets(currentP).multi_textured = False
        Dim primStart As Integer = InStr(TheXML_String, "primitiveGroup>" & primNum.ToString)
        Dim primStart2 As Integer = InStr(primStart + 5, TheXML_String, "primitiveGroup>")
        If primStart2 = 0 Then primStart2 = TheXML_String.Length
        Dim diff_pos As Integer
        'Dim spec_pos As Integer
        'Dim norm_pos As Integer
        If primStart = 0 Then primStart += 1
        diff_pos = InStr(primStart, TheXML_String, "diffuseMap<")
        If diff_pos = 0 Then
            'No diffuseMap name was found. This means this primitiveGroup
            'is probable something we dont want or need.. :)
            Return False
        End If
        If diff_pos > 0 Then
            If diff_pos > primStart2 Then
                'diff_pos has locked on to the next primitiveGroups diffuseMap
                'because this group has NONE.. this means its a collision box
                'and we dont want to waste our time on these... :)
                GoTo clouds

            End If
            Dim tex1_pos = InStr(diff_pos, TheXML_String, "<Texture>") + "<texture>".Length
            Dim tex1_Epos = InStr(tex1_pos, TheXML_String, "</Texture>")
            Dim newS As String = ""
            newS = Mid(TheXML_String, tex1_pos, tex1_Epos - tex1_pos)
            Models.models(mod_id).componets(currentP).color_name = newS
            'Debug.Write(newS & vbCrLf)
            Models.models(mod_id).componets(currentP).color_id = -1
            If newS.Contains("clouds.dds") Then
                Return True
            End If
        End If
        '----------------------------------------------------------------------------------------
clouds:
        diff_pos = InStr(primStart, TheXML_String, "clouds<")
        If diff_pos > 0 Then
            If diff_pos > primStart2 Then
                'diff_pos has locked on to the next primitiveGroups diffuseMap
                'because this group has NONE.. this means its a collision box
                'and we dont want to waste our time on these... :)
                Return False
            End If
            Dim tex1_pos = InStr(diff_pos, TheXML_String, "<Texture>") + "<texture>".Length
            Dim tex1_Epos = InStr(tex1_pos, TheXML_String, "</Texture>")
            Dim newS As String = ""
            newS = Mid(TheXML_String, tex1_pos, tex1_Epos - tex1_pos)
            Models.models(mod_id).componets(currentP).color_name = newS
            'Debug.Write(newS & vbCrLf)
            Models.models(mod_id).componets(currentP).color_id = -1
            Return True
        End If
        If Models.models(mod_id).componets(currentP).multi_textured Then
            Models.models(mod_id).componets(currentP).color2_name = "none"
            diff_pos = InStr(primStart, TheXML_String, "diffuseMap2<")
            If diff_pos > primStart2 Then
                'diff_pos has locked on to the next primitiveGroups diffuseMap
                'because this group has NONE.. this means its a collision box
                'and we dont want to waste our time on these... :)
                Models.models(mod_id).componets(currentP).multi_textured = False
                'Return True

            End If
            If diff_pos > 0 Then
                Dim tex1_pos = InStr(diff_pos, TheXML_String, "<Texture>") + "<texture>".Length
                Dim tex1_Epos = InStr(tex1_pos, TheXML_String, "</Texture>")
                Dim newS As String = ""
                newS = Mid(TheXML_String, tex1_pos, tex1_Epos - tex1_pos)
                Models.models(mod_id).componets(currentP).color2_name = newS
                Models.models(mod_id).componets(currentP).color2_Id = -1
                'Debug.Write(newS & vbCrLf)
            Else
                has_uv2 = False
                Models.models(mod_id).componets(currentP).multi_textured = False
            End If
        End If
        'saving this shit incase we want to use it to find the bump-normals later..
        'Not likely.. it will slow the rendering to a crawl!
        Models.models(mod_id).componets(currentP).bumped = False    ' this stops loading NormalMaps

        ' if we dont want bump mapped models.. lets not waste time loading them!!!
        model_bump_loaded = True
        diff_pos = InStr(primStart, TheXML_String, "normalMap<")
        If diff_pos > 0 Then
            If diff_pos > primStart2 Then
                'diff_pos has locked on to the next primitiveGroups diffuseMap
                'because this group has NONE.. this means its a collision box
                'and we dont want to waste our time on these... :)
                Models.models(mod_id).componets(currentP).bumped = False
                Return True
            End If
            Dim tex1_pos = InStr(diff_pos, TheXML_String, "<Texture>") + "<texture>".Length
            Dim tex1_Epos = InStr(tex1_pos, TheXML_String, "</Texture>")
            Dim newS As String = ""
            newS = Mid(TheXML_String, tex1_pos, tex1_Epos - tex1_pos)
            'Dim ar = Models.models(mod_id).componets(currentP).color_name
            Models.models(mod_id).componets(currentP).normal_name = newS
            'Debug.Write(newS & vbCrLf)
            Models.models(mod_id).componets(currentP).normal_Id = -1
            Models.models(mod_id).componets(currentP).bumped = True
        End If
        diff_pos = InStr(primStart, TheXML_String, "alphaReference<")
        If diff_pos > 0 Then
            If diff_pos > primStart2 Then
                'diff_pos has locked on to the next primitiveGroups diffuseMap
                'because this group has NONE.. this means its a collision box
                'and we dont want to waste our time on these... :)
                Models.models(mod_id).componets(currentP).alphaRef = 0
                Return True
            End If
            Dim tex1_pos = InStr(diff_pos, TheXML_String, "<Int>") + "<Int>".Length
            Dim tex1_Epos = InStr(tex1_pos, TheXML_String, "</Int>")
            Dim newS As String = ""
            newS = Mid(TheXML_String, tex1_pos, tex1_Epos - tex1_pos)
            'Dim ar = Models.models(mod_id).componets(currentP).color_name
            Dim ref = Convert.ToInt32(newS)

            Models.models(mod_id).componets(currentP).alphaRef = ref

        End If
        diff_pos = InStr(primStart, TheXML_String, "alphaTestEnable<")
        If diff_pos > 0 Then
            If diff_pos > primStart2 Then
                'diff_pos has locked on to the next primitiveGroups diffuseMap
                'because this group has NONE.. this means its a collision box
                'and we dont want to waste our time on these... :)
                Models.models(mod_id).componets(currentP).alphaRef = 0
                Return True
            End If
            Dim tex1_pos = InStr(diff_pos, TheXML_String, "<Bool>") + "<Bool>".Length
            Dim tex1_Epos = InStr(tex1_pos, TheXML_String, "</Bool>")
            Dim newS As String = ""
            newS = Mid(TheXML_String, tex1_pos, tex1_Epos - tex1_pos)
            'Dim ar = Models.models(mod_id).componets(currentP).color_name
            Dim ref As Integer = 0
            If newS = "true" Then
                ref = 1
            End If
            Models.models(mod_id).componets(currentP).alphaTestEnable = ref


        End If

        Return True 'have a valid texture name.. Whaaaahooooooo!!!


    End Function

End Module
