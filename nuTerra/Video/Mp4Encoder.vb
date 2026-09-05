Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices
Imports Vortice.MediaFoundation

''' <summary>
''' Assembles a captured PNG sequence into an H.264 mp4, using the encoder that
''' is already part of Windows.
'''
''' Media Foundation rather than a bundled ffmpeg: the encoder ships with the
''' OS, so nothing has to be shipped beside the exe and nobody building this
''' has to install a binary and put it on PATH. Vortice.MediaFoundation is only
''' the binding - a NuGet reference that restores like any other - and on a
''' machine with an NVIDIA or Intel GPU the work lands on the hardware encoder.
'''
''' The trade is quality per bit: this is worse than x264 at the same bitrate.
''' That is bought back with a generous bitrate rather than with a dependency,
''' and at 1080p the difference is not what anyone will notice about the video.
''' </summary>
Public Module Mp4Encoder

    ' 100 nanosecond ticks in a second - Media Foundation's time unit.
    Private Const HNS_PER_SECOND As Long = 10000000L

    ' Bits per pixel per frame. The encoder is handed a target bitrate, not a
    ' quality, so the rate has to carry the quality: 0.20 is generous for 1080p
    ' and leaves foliage and rock detail intact, where the 0.05 to 0.10 typical
    ' of streaming video visibly smears exactly the terrain this exists to show.
    Private Const BITS_PER_PIXEL As Double = 0.2

    Private Const MIN_BITRATE As Integer = 10000000
    Private Const MAX_BITRATE As Integer = 60000000

    ''' <summary>
    ''' Encode every frame_*.png in a folder into one mp4 beside them.
    '''
    ''' Returns Nothing on success, or a message describing what stopped it.
    ''' Runs synchronously and takes minutes - call it on a background thread.
    ''' </summary>
    ''' <param name="progress">Called with the count done so far. May be Nothing.</param>
    Public Function EncodeFolder(dir As String, fps As Integer, out_path As String,
                                 progress As Action(Of Integer)) As String

        Dim files As String()
        Try
            files = IO.Directory.GetFiles(dir, "frame_*.png")
        Catch ex As Exception
            Return "cannot read " & dir & " - " & ex.Message
        End Try

        If files.Length = 0 Then Return "no frames in " & dir
        ' GetFiles does not promise an order. The names are zero padded, so an
        ' ordinal sort is the frame order - but only because they are padded,
        ' which is worth not relying on silently.
        Array.Sort(files, StringComparer.Ordinal)

        ' Dimensions come from the first frame rather than from the window that
        ' shot them: this may be run over a folder captured in an earlier
        ' session, at a size the app is no longer running at.
        Dim w As Integer, h As Integer
        Try
            Using probe = Image.FromFile(files(0))
                w = probe.Width
                h = probe.Height
            End Using
        Catch ex As Exception
            Return "cannot read " & IO.Path.GetFileName(files(0)) & " - " & ex.Message
        End Try

        ' H.264 has no odd dimensions. The capture path rounds down so this
        ' should never fire, but a folder can hold frames from any source.
        If (w And 1) <> 0 OrElse (h And 1) <> 0 Then
            Return String.Format("{0}x{1} has an odd dimension - H.264 needs both even", w, h)
        End If

        Dim bitrate = CInt(Math.Min(MAX_BITRATE,
                       Math.Max(MIN_BITRATE, w * h * CDbl(fps) * BITS_PER_PIXEL)))
        Dim frame_bytes = w * h * 4
        Dim duration = HNS_PER_SECOND \ CLng(Math.Max(1, fps))

        Dim started = False
        Dim writer As IMFSinkWriter = Nothing

        Try
            ' Lite: no sockets, no network source. This only writes a file.
            MediaFactory.MFStartup(True)
            started = True

            ' Let the encoder run on the GPU when there is one. Without this the
            ' whole sequence goes through the software encoder, which for a
            ' couple of thousand 1080p frames is the difference between a wait
            ' and a very long wait.
            Dim attrs = MediaFactory.MFCreateAttributes(1)
            attrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1UI)

            ' The .mp4 extension is what picks the MPEG-4 file sink.
            writer = MediaFactory.MFCreateSinkWriterFromURL(out_path, Nothing, attrs)

            ' What comes OUT: H.264 at the target rate.
            Dim out_type = MediaFactory.MFCreateMediaType()
            out_type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video)
            out_type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264)
            out_type.Set(MediaTypeAttributeKeys.AvgBitrate, CUInt(bitrate))
            out_type.Set(MediaTypeAttributeKeys.InterlaceMode, CUInt(VideoInterlaceMode.Progressive))
            MediaFactory.MFSetAttributeSize(out_type, MediaTypeAttributeKeys.FrameSize, CUInt(w), CUInt(h))
            MediaFactory.MFSetAttributeRatio(out_type, MediaTypeAttributeKeys.FrameRate, CUInt(fps), 1UI)
            MediaFactory.MFSetAttributeRatio(out_type, MediaTypeAttributeKeys.PixelAspectRatio, 1UI, 1UI)
            Dim stream_index = writer.AddStream(out_type)

            ' What goes IN: 32 bit BGRA, one frame per buffer. The sink writer
            ' inserts the colour converter to NV12 itself, so nothing here has
            ' to know what the encoder wants.
            Dim in_type = MediaFactory.MFCreateMediaType()
            in_type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video)
            in_type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32)
            in_type.Set(MediaTypeAttributeKeys.InterlaceMode, CUInt(VideoInterlaceMode.Progressive))
            ' POSITIVE stride, which means top-down rows.
            '
            ' RGB32 in Media Foundation follows the DIB convention and is
            ' bottom-up by default. The PNGs were already flipped upright when
            ' they were written, so without saying this the entire video comes
            ' out upside down - and it is the kind of wrong that is only found
            ' after the encode, never during it.
            in_type.Set(MediaTypeAttributeKeys.DefaultStride, CUInt(w * 4))
            MediaFactory.MFSetAttributeSize(in_type, MediaTypeAttributeKeys.FrameSize, CUInt(w), CUInt(h))
            MediaFactory.MFSetAttributeRatio(in_type, MediaTypeAttributeKeys.FrameRate, CUInt(fps), 1UI)
            MediaFactory.MFSetAttributeRatio(in_type, MediaTypeAttributeKeys.PixelAspectRatio, 1UI, 1UI)
            writer.SetInputMediaType(stream_index, in_type, Nothing)

            writer.BeginWriting()

            ' One managed staging array for the whole run. Marshal.Copy cannot
            ' go native to native, and allocating 8 MB per frame would hand the
            ' GC a couple of thousand large-object allocations for nothing.
            Dim staging(frame_bytes - 1) As Byte
            Dim timestamp As Long = 0

            For i = 0 To files.Length - 1
                Using bmp As New Bitmap(files(i))
                    If bmp.Width <> w OrElse bmp.Height <> h Then
                        Return String.Format("{0} is {1}x{2}, expected {3}x{4} - the folder holds frames from more than one capture",
                                             IO.Path.GetFileName(files(i)), bmp.Width, bmp.Height, w, h)
                    End If

                    ' Locking as 32bpp converts from the file's 24bpp on the way
                    ' out, so the widening costs nothing extra here.
                    Dim bits = bmp.LockBits(New Rectangle(0, 0, w, h),
                                            ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb)
                    Try
                        Marshal.Copy(bits.Scan0, staging, 0, frame_bytes)
                    Finally
                        bmp.UnlockBits(bits)
                    End Try
                End Using

                Dim buffer = MediaFactory.MFCreateMemoryBuffer(frame_bytes)
                Dim dst As IntPtr, max_len As Integer, cur_len As Integer
                buffer.Lock(dst, max_len, cur_len)
                Try
                    Marshal.Copy(staging, 0, dst, frame_bytes)
                Finally
                    buffer.Unlock()
                End Try
                buffer.CurrentLength = frame_bytes

                Dim sample = MediaFactory.MFCreateSample()
                sample.AddBuffer(buffer)
                sample.SampleTime = timestamp
                sample.SampleDuration = duration
                writer.WriteSample(stream_index, sample)
                timestamp += duration

                sample.Dispose()
                buffer.Dispose()

                If progress IsNot Nothing Then progress(i + 1)
            Next

            ' Writes the index and closes the container. Skipping this leaves a
            ' file with frames in it and no moov atom, which no player will open
            ' - the same failure a half finished encode shows.
            finalize_writer(writer)

        Catch ex As Exception
            Return ex.Message
        Finally
            If writer IsNot Nothing Then
                Try : writer.Dispose() : Catch : End Try
            End If
            If started Then
                Try : MediaFactory.MFShutdown() : Catch : End Try
            End If
        End Try

        Return Nothing
    End Function

    ''' <summary>
    ''' Call IMFSinkWriter::Finalize.
    '''
    ''' Through reflection because VB will not let Finalize be called by name -
    ''' it reserves it for the destructor on Object, and refuses the interface
    ''' member that happens to share the name. This is the whole reason for the
    ''' indirection; there is nothing subtle going on.
    ''' </summary>
    Private Sub finalize_writer(writer As IMFSinkWriter)
        Dim m = GetType(IMFSinkWriter).GetMethod("Finalize")
        If m Is Nothing Then Throw New InvalidOperationException(
            "IMFSinkWriter.Finalize not found - the mp4 would have no index")
        m.Invoke(writer, Nothing)
    End Sub

End Module
