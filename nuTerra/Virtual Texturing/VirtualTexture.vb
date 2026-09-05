Imports OpenTK.Mathematics
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' Based on: http://linedef.com/virtual-texture-demo.html
''' </summary>

Public Class VirtualTexture
    Implements IDisposable

    ReadOnly indexer As PageIndexer
    ReadOnly pagetable As PageTable
    ReadOnly atlas As TextureAtlas
    ReadOnly loader As PageLoader
    ReadOnly cache As PageCache

    ReadOnly num_tiles As Integer
    ReadOnly uploadsperframe As Integer

    ReadOnly toload As List(Of PageCount)

    ' Requested mip is floor(MipLevel(uv) - MipBias), so a bigger bias asks for
    ' finer pages. Near the camera the result already clamps at mip 0, which is
    ' why raising this sharpens the distance and leaves the foreground alone.
    Const MAX_MIP_BIAS As Integer = 6
    Const MIN_MIP_BIAS As Integer = 0

    Dim _mipbias As Integer = MAX_MIP_BIAS

    Public Property MipBias As Integer
        Get
            Return _mipbias
        End Get
        Set
            _mipbias = Value
            If VT_BAKE_TRACE Then
                LogThis("MipBias: {0}", _mipbias)
                Console.Out.Flush()
            End If
        End Set
    End Property

    Public Sub New(info As VirtualTextureInfo, num_tiles As Integer, uploadsperframe As Integer)
        Me.num_tiles = num_tiles
        Me.uploadsperframe = uploadsperframe

        indexer = New PageIndexer(info)
        toload = New List(Of PageCount)(indexer.Count)
        atlas = New TextureAtlas(info, num_tiles)
        loader = New PageLoader(indexer, info)
        cache = New PageCache(atlas, loader, num_tiles)
        pagetable = New PageTable(cache, info)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        pagetable.Dispose()
        atlas.Dispose()
        loader.Dispose()
    End Sub

    Public Sub Bind()
#If False Then
        ' SHOULD WE USE MULTI BIND?
        Dim textures() = {
            pagetable.texture.texture_id,
            atlas.color_texture.texture_id,
            atlas.normal_texture.texture_id,
            atlas.specular_texture.texture_id
            }
        GL.BindTextures(0, 4, textures)
#Else
        pagetable.texture.BindUnit(0)
        atlas.color_texture.BindUnit(1)
        atlas.normal_texture.BindUnit(2)
        atlas.specular_texture.BindUnit(3)
#End If
    End Sub

    Public Sub Unbind()
        unbind_textures(4)
    End Sub

    Public Sub DebugDraw(location As Point, size As Point, proj As Matrix4)
        atlas.color_texture.BindUnit(0)

        image2dArrayShader.Use()
        GL.UniformMatrix4(image2dArrayShader("ProjectionMatrix"), False, proj)

        Dim rect = New RectangleF(location.X, location.Y, size.X / 10, size.X / 10)
        GL.Uniform4(image2dArrayShader("rect"),
                    rect.Left,
                    -rect.Bottom,
                    rect.Right,
                    -rect.Top)

        GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, num_tiles)

        image2dArrayShader.StopUse()

        ' UNBIND
        GL.BindTextureUnit(0, 0)
    End Sub

    Public Sub Clear()
        cache.Clear()
    End Sub

    Dim trace_frame As Integer

    Private _settled_frames As Integer

    ''' <summary>
    ''' How many CONSECUTIVE Update calls have had nothing left to fetch. Zero
    ''' means work is outstanding; a long run means the terrain has stopped
    ''' changing.
    '''
    ''' Counted inside Update on purpose, and not readable as a bare boolean,
    ''' because every cheaper test is wrong:
    '''
    ''' - toload.Count = 0 alone is wrong. It empties one frame BEFORE the bias
    '''   ratchets a step finer, and that step immediately asks for a fresh set
    '''   of pages, so it reads settled one refinement early.
    '''
    ''' - Adding the bias test is STILL wrong on its own, and this was the real
    '''   bug: _mipbias is initialised to MAX_MIP_BIAS and toload starts empty,
    '''   so before a single page has streamed both halves are already true. A
    '''   capture gated on it fired on frame one, against bare terrain.
    '''
    ''' requests.Count > 0 is what rules that out - it means the feedback buffer
    ''' has actually reported on a rendered frame, so there is something for
    ''' "nothing outstanding" to be a statement about. And counting a RUN rather
    ''' than an instant covers the readback lag: the feedback buffer is a frame
    ''' or more behind the render, so one settled report describes an older view.
    ''' </summary>
    Public ReadOnly Property SettledFrames As Integer
        Get
            Return _settled_frames
        End Get
    End Property

    ''' <summary>
    ''' Throw away the settled run, because the view it described no longer
    ''' exists.
    '''
    ''' SettledFrames is a statement about a CAMERA POSITION, not about the
    ''' cache: it counts Updates during which the current view asked for
    ''' nothing missing. Teleport the camera and that count is still describing
    ''' where it used to be, and stays high for as long as the feedback buffer
    ''' takes to catch up - which is a frame or more, because the readback is
    ''' behind the render by design.
    '''
    ''' That window is small and it is exactly where a capture starts. Rewinding
    ''' to the head of a flight path and shooting immediately gave a first frame
    ''' whose terrain had never been requested, waved through by a counter that
    ''' had been earned somewhere else entirely.
    '''
    ''' Anything that moves the camera discontinuously should call this. Ordinary
    ''' flying must not - the count is only meaningful because it is consecutive,
    ''' and resetting it every frame would mean it never reached the run length
    ''' anything waits for.
    ''' </summary>
    Public Sub ResetSettled()
        _settled_frames = 0
    End Sub

    Public Sub Update(requests As Dictionary(Of Page, Integer))
        toload.Clear()

        ' Find out what is already in memory
        ' If it Is, update it's position in the LRU collection
        ' Otherwise add it to the list of pages to load
        Dim touched = 0
        For Each req In requests
            Dim pc = New PageCount With {
                .Page = req.Key,
                .Count = req.Value
            }

            If Not cache.Touch(pc.Page) Then
                toload.Add(pc)
            Else
                touched += 1
            End If
        Next

        trace_frame += 1
        If VT_BAKE_TRACE AndAlso trace_frame Mod 120 = 0 Then
            Dim cam = map_scene.camera.CAM_POSITION
            LogThis("vt frame: requests={0} touched={1} toload={2} bias={3} cam={4:0.000},{5:0.000},{6:0.000}",
                    requests.Count, touched, toload.Count, _mipbias, cam.X, cam.Y, cam.Z)
            Console.Out.Flush()
        End If

        ' Check to make sure we don't thrash
        If touched < num_tiles Then
            ' sort by low res to high res And number of requests
            toload.Sort()

            ' if more pages than will fit in memory or more than update per frame drop high res pages with lowest use count
            Dim loadcount = Math.Min(Math.Min(toload.Count, uploadsperframe), num_tiles)
            For i = 0 To loadcount - 1
                cache.Request(toload(i).Page)
            Next

            ' Nothing left to fetch and the cache still has room, so we can afford
            ' to ask for finer pages again. Without this the backoff below only
            ' ever ratchets one way and the terrain never recovers its sharpness
            ' after a single busy frame.
            If toload.Count = 0 AndAlso _mipbias < MAX_MIP_BIAS Then
                MipBias += 1
            End If
        Else
            ' the working set does not fit - back off to coarser pages, but not
            ' past the point where the bias stops meaning anything
            If _mipbias > MIN_MIP_BIAS Then
                MipBias -= 1
            End If
        End If

        ' Has anything been asked for that is not here yet? See SettledFrames.
        If requests.Count > 0 AndAlso toload.Count = 0 AndAlso _mipbias >= MAX_MIP_BIAS Then
            _settled_frames += 1
        Else
            _settled_frames = 0
        End If

        ' Update the page table
        pagetable.Update()
    End Sub
End Class
