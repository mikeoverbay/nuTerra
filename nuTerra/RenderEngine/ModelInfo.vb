Imports System.Reflection
Imports System.Text
Imports OpenTK.Mathematics

''' <summary>
''' Everything known about a picked model, as text.
'''
''' The static half has to be gathered DURING the load. MAP_MODELS is erased at
''' the end of it (MapLoader, right after BulbPlacer.CollectLightModels), so by
''' the time anyone double-clicks a model the render sets, their primitive
''' groups and the material behind each group are all gone. What outlives the
''' load is MODEL_BATCH_LIST and MODEL_INDEX_LIST - the instance transforms -
''' so the per-INSTANCE half is resolved on demand and only the per-MODEL half
''' is kept.
'''
''' Materials are dumped by reflection rather than a field list per shader
''' family: props is one of a dozen MaterialProps_* structures, and a hand
''' written switch would be a dozen places to forget when one of them grows a
''' field. "All info" means all of it.
''' </summary>
Public Class ModelInfo

    ''' <summary>model_id -> the static report, built once per model.</summary>
    Private Shared FACTS As New Dictionary(Of Integer, String)

    ''' <summary>pick instance -> (model_id, index into MODEL_INDEX_LIST).</summary>
    Private Shared OWNER As New Dictionary(Of UInteger, Integer)
    Private Shared XFORM As New Dictionary(Of UInteger, Integer)

    Public Shared Sub Reset()
        FACTS.Clear()
        OWNER.Clear()
        XFORM.Clear()
        INSTANCE_OF = Nothing
    End Sub

    ''' <summary>
    ''' Called once per batch from the loader, while MAP_MODELS is still alive.
    ''' first_instance is the same counter PICK_DICTIONARY is keyed by.
    ''' </summary>
    ''' <summary>
    ''' The GPU instance carrying a given MODEL_INDEX_LIST entry, or -1.
    '''
    ''' XFORM runs the other way - instance to transform - because that is the
    ''' direction a PICKED instance needs. The lamp shadow bake needs this one:
    ''' a bulb light knows which transform placed it, and has to name the
    ''' instance to leave out of its own cube. The two index spaces are NOT the
    ''' same - MapLoader fills the instance buffer at a running mLast while it
    ''' reads transforms at batch.offset - so one cannot stand in for the other.
    '''
    ''' Built on first ask and dropped with the rest of the tables on Clear.
    ''' </summary>
    Private Shared INSTANCE_OF As Dictionary(Of Integer, Integer) = Nothing

    Public Shared Function instance_of_xform(xform_index As Integer) As Integer
        If INSTANCE_OF Is Nothing Then
            INSTANCE_OF = New Dictionary(Of Integer, Integer)(XFORM.Count)
            For Each kv In XFORM
                INSTANCE_OF(kv.Value) = CInt(kv.Key)
            Next
        End If
        Dim inst As Integer
        If INSTANCE_OF.TryGetValue(xform_index, inst) Then Return inst
        Return -1
    End Function

    Public Shared Sub Capture(model_id As Integer, first_instance As Integer,
                              first_xform As Integer, count As Integer,
                              model_dir As String)
        For i = 0 To count - 1
            OWNER(CUInt(first_instance + i)) = model_id
            XFORM(CUInt(first_instance + i)) = first_xform + i
        Next
        If FACTS.ContainsKey(model_id) Then Return
        FACTS(model_id) = build_model_facts(model_id, count, model_dir)
    End Sub

    Private Shared Function build_model_facts(model_id As Integer, count As Integer,
                                             model_dir As String) As String
        Dim sb As New StringBuilder()
        Try
            Dim m = MAP_MODELS(model_id)
            sb.AppendLine("model_id      : " & model_id.ToString())
            sb.AppendLine("directory     : " & If(model_dir, "(none)"))
            sb.AppendLine("instances     : " & count.ToString())

            Dim b0 = m.visibilityBounds.Row0
            Dim b1 = m.visibilityBounds.Row1
            sb.AppendLine(String.Format("bounds min    : {0:0.000}, {1:0.000}, {2:0.000}", b0.X, b0.Y, b0.Z))
            sb.AppendLine(String.Format("bounds max    : {0:0.000}, {1:0.000}, {2:0.000}", b1.X, b1.Y, b1.Z))
            sb.AppendLine(String.Format("size (m)      : {0:0.000} x {1:0.000} x {2:0.000}",
                                        Math.Abs(b1.X - b0.X), Math.Abs(b1.Y - b0.Y), Math.Abs(b1.Z - b0.Z)))

            Dim lods = m.modelLods
            sb.AppendLine("LODs          : " & If(lods Is Nothing, 0, lods.Length).ToString())
            If lods IsNot Nothing Then
                For li = 0 To lods.Length - 1
                    Dim lod = lods(li)
                    sb.AppendLine("")
                    sb.AppendLine("--- LOD " & li.ToString() & " ---")
                    If lod Is Nothing Then
                        sb.AppendLine("  (null)")
                        Continue For
                    End If
                    sb.AppendLine("  primitive_name : " & If(lod.primitive_name, "(none)"))
                    sb.AppendLine("  junk           : " & lod.junk.ToString())
                    Dim sets = lod.render_sets
                    sb.AppendLine("  render sets    : " & If(sets Is Nothing, 0, sets.Count).ToString())
                    If sets Is Nothing Then Continue For
                    For si = 0 To sets.Count - 1
                        append_render_set(sb, sets(si), si)
                    Next
                Next
            End If
        Catch ex As Exception
            sb.AppendLine("(failed to read model facts: " & ex.Message & ")")
        End Try
        Return sb.ToString()
    End Function

    Private Shared Sub append_render_set(sb As StringBuilder, rs As RenderSetEntry, si As Integer)
        sb.AppendLine("")
        sb.AppendLine("  [render set " & si.ToString() & "]")
        If rs Is Nothing Then
            sb.AppendLine("    (null)")
            Return
        End If
        sb.AppendLine("    verts_name   : " & If(rs.verts_name, "(none)"))
        sb.AppendLine("    prims_name   : " & If(rs.prims_name, "(none)"))
        sb.AppendLine("    numVertices  : " & rs.numVertices.ToString())
        sb.AppendLine("    element_count: " & rs.element_count.ToString())
        sb.AppendLine("    has_tangent  : " & rs.has_tangent.ToString())
        sb.AppendLine("    no_draw      : " & rs.no_draw.ToString())

        ' Whether this mesh carries a SECOND UV set, which the tiled-atlas
        ' families need: uv1 is the repeating detail tile, uv2 is the
        ' per-object unwrap that the blend mask, the dirt and the global
        ' colour/normal maps are all keyed to. PrimitiveLoader only calls
        ' load_primitives_uv2 when the .uv2 section exists, so without it TC2
        ' is zero everywhere - the blend mask reads one constant texel, one
        ' tile wins the whole surface, and the object comes out flat.
        If rs.buffers Is Nothing Then
            sb.AppendLine("    uv2 (TC2)    : (no buffers)")
        ElseIf rs.buffers.uv2 Is Nothing Then
            sb.AppendLine("    uv2 (TC2)    : ABSENT - TC2 is 0, blend/global maps sample one texel")
        Else
            sb.AppendLine("    uv2 (TC2)    : " & rs.buffers.uv2.Length.ToString() & " entries")
        End If

        If rs.primitiveGroups Is Nothing Then
            sb.AppendLine("    primitiveGroups: (none)")
            Return
        End If
        sb.AppendLine("    primitiveGroups: " & rs.primitiveGroups.Count.ToString())
        For Each kv In rs.primitiveGroups
            Dim pg = kv.Value
            sb.AppendLine("      group " & kv.Key.ToString() &
                          ": startIndex=" & pg.startIndex.ToString() &
                          " nPrimitives=" & pg.nPrimitives.ToString() &
                          " startVertex=" & pg.startVertex.ToString() &
                          " nVertices=" & pg.nVertices.ToString() &
                          " no_draw=" & pg.no_draw.ToString())
            append_material(sb, pg.material_id)
        Next
    End Sub

    Private Shared Sub append_material(sb As StringBuilder, material_id As Integer)
        sb.AppendLine("        material_id : " & material_id.ToString())
        If materials Is Nothing Then
            sb.AppendLine("        (no material table)")
            Return
        End If
        Dim mat As Material = Nothing
        If Not materials.TryGetValue(CUInt(material_id), mat) Then
            sb.AppendLine("        (material not in the table)")
            Return
        End If
        sb.AppendLine("        shader      : " & mat.shader_type.ToString() &
                      " (" & CInt(mat.shader_type).ToString() & ")")
        If mat.props Is Nothing Then
            sb.AppendLine("        props       : (none)")
            Return
        End If

        ' Reflection, so a MaterialProps_* structure that grows a field shows
        ' the new field here without anyone remembering to come back.
        Dim t = mat.props.GetType()
        sb.AppendLine("        props       : " & t.Name)
        For Each f As FieldInfo In t.GetFields(BindingFlags.Public Or BindingFlags.Instance)
            Dim v As Object = Nothing
            Try
                v = f.GetValue(mat.props)
            Catch
                v = "(unreadable)"
            End Try
            sb.AppendLine("          " & f.Name.PadRight(24) & " = " & If(v Is Nothing, "(nothing)", v.ToString()))
        Next
    End Sub

    ''' <summary>
    ''' The full report for one picked instance, static facts plus where this
    ''' particular copy of the model stands. Empty string if nothing is known,
    ''' which is what a tree pick or a stale index gives.
    ''' </summary>
    Public Shared Function Report(pick_id As UInteger) As String
        If pick_id = 0 Then Return ""
        Dim inst = pick_id - 1UI

        Dim model_id As Integer
        If Not OWNER.TryGetValue(inst, model_id) Then Return ""

        Dim sb As New StringBuilder()
        sb.AppendLine("=== picked instance ===")
        sb.AppendLine("pick id       : " & pick_id.ToString())
        sb.AppendLine("instance      : " & inst.ToString())

        Dim xi As Integer
        If XFORM.TryGetValue(inst, xi) AndAlso MODEL_INDEX_LIST IsNot Nothing AndAlso
           xi >= 0 AndAlso xi < MODEL_INDEX_LIST.Length Then
            Dim mtx = MODEL_INDEX_LIST(xi).matrix
            Dim p = mtx.Row3.Xyz
            sb.AppendLine(String.Format("world position: {0:0.000}, {1:0.000}, {2:0.000}", p.X, p.Y, p.Z))
            sb.AppendLine("matrix        :")
            sb.AppendLine(String.Format("  {0,10:0.0000} {1,10:0.0000} {2,10:0.0000} {3,10:0.0000}", mtx.M11, mtx.M12, mtx.M13, mtx.M14))
            sb.AppendLine(String.Format("  {0,10:0.0000} {1,10:0.0000} {2,10:0.0000} {3,10:0.0000}", mtx.M21, mtx.M22, mtx.M23, mtx.M24))
            sb.AppendLine(String.Format("  {0,10:0.0000} {1,10:0.0000} {2,10:0.0000} {3,10:0.0000}", mtx.M31, mtx.M32, mtx.M33, mtx.M34))
            sb.AppendLine(String.Format("  {0,10:0.0000} {1,10:0.0000} {2,10:0.0000} {3,10:0.0000}", mtx.M41, mtx.M42, mtx.M43, mtx.M44))
        Else
            sb.AppendLine("world position: (no transform for this instance)")
        End If

        sb.AppendLine("")
        sb.AppendLine("=== model ===")
        ' Not named "facts" - VB is case insensitive, so it would shadow the
        ' shared FACTS dictionary and the lookup below resolves against a String.
        Dim static_part As String = Nothing
        If FACTS.TryGetValue(model_id, static_part) Then
            sb.Append(static_part)
        Else
            sb.AppendLine("(nothing captured for model_id " & model_id.ToString() & ")")
        End If
        Return sb.ToString()
    End Function

End Class
