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


End Module
