
Imports System.IO
Imports System.IO.MemoryMappedFiles
Imports System.Runtime.InteropServices
Imports System.Security
Imports System.Security.Permissions

Module ModTriangleHolderClass
    <SecurityCriticalAttribute>
  <SecurityPermissionAttribute(SecurityAction.Demand, Flags:=SecurityPermissionFlag.UnmanagedCode)>
    Public Class mappedFile_
        Private mmf As MemoryMappedFile
        Private accessor As MemoryMappedViewAccessor
        Private fpath As String
        Private vd As New t_holder_
        Private index_ As Integer
        Private vdsize As Integer
        Default Public Property Indexer(ByVal index As Integer) As t_holder_
            Set(value As t_holder_)
                accessor.Write(index * Me.vdsize, value)
                GC.KeepAlive(mmf)
            End Set
            Get
                accessor.Read(index * Me.vdsize, Me.vd)
                Return Me.vd
            End Get
        End Property

        Public Property v(ByVal index As Integer) As vertex_data

            Get
                accessor.Read(index * Me.vdsize, Me.vd)
                Return Me.vd.v
            End Get
            Set(value As vertex_data)
                accessor.Read(index * Me.vdsize, Me.vd)
                Me.vd.v = value
                accessor.Write(index * Me.vdsize, Me.vd)
                Return
            End Set
        End Property
        Public Property mesh_location(ByVal index As Integer) As Integer
            Get
                accessor.Read(index * Me.vdsize, Me.vd)
                Return Me.vd.mesh_location
            End Get
            Set(value As Integer)
                accessor.Read(index * Me.vdsize, Me.vd)
                Me.vd.mesh_location = value
                accessor.Write(index * Me.vdsize, Me.vd)
                Return
            End Set
        End Property

        Public Sub open(ByVal size As Integer)
            Dim Ms As New MemoryMappedFileSecurity
            size += 1
            Me.vdsize = Marshal.SizeOf(GetType(t_holder_))

            mmf = MemoryMappedFile.CreateOrOpen("data.bin", vdsize * size)
            accessor = mmf.CreateViewAccessor(0.0, size * vdsize, MemoryMappedFileAccess.ReadWrite)
            For i = 0 To size - 1
                accessor.Write(i * Me.vdsize, vd)
            Next
        End Sub
    End Class

    Public Structure t_holder_
        Dim v As vertex_data
        Dim mesh_location As Integer
    End Structure

End Module
