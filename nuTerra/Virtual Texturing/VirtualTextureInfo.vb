Imports System.Runtime.InteropServices

<StructLayout(LayoutKind.Sequential, Pack:=1)>
Public Structure VirtualTextureInfo
	Public VirtualTextureSize As Integer
	Public TileSize As Integer

	ReadOnly Property PageTableSize As Integer
		Get
			Return VirtualTextureSize \ TileSize
		End Get
	End Property
End Structure
