Imports OpenTK
Imports OpenTK.Graphics.OpenGL

Module Explosion_Types

    Private rand As New Random

    Public Function get_random_vector3(ByVal scaler As Single) As Vector3
        Dim v As New Vector3
        v.X = CSng((rand.NextDouble - 0.5)) * scaler
        v.Y = CSng((rand.NextDouble - 0.5)) * scaler
        v.Z = CSng((rand.NextDouble - 0.5)) * scaler
        Return v
    End Function

    Public Structure Explosion_type_1
        Private time_delta As Int64
        Private timer As Stopwatch

        Private start_location_ As Vector3
        Private Scatter_factor_ As Single ' particle scatter expansion speed
        Private expand_speed_ As Single 'image grow speed
        Private expand_start_size_ As Single 'inital billboard size
        Private max_expand_size_ As Single 'max billboard size
        Private fade_time_ As Single
        Private update_time_ As Single
        Private particle_count_ As Integer
        Private birth_speed_ As Int64 'delay in milliseconds
        Private continuous_ As Boolean
        Private Fixed_expand_speed_ As Boolean
        Private image_atlas_id_ As Integer
        Private Note_ As String

        Private total_frames_ As Single
        Private row_length_ As Single

        Private DONE As Boolean
        Private done_count As Integer

        Public particles() As particle_


        Public Structure particle_
            Public location As Vector3
            Public rotation_angle As Single
            Public scatter_direction As Vector3
            Public expand_scale As Single
            Public expand_factor As Single
            Public this_particle_time As Int64
            Public frame_index As Single
            Public aLive As Boolean
            Public total_frames As Single
            Public row_length As Single
        End Structure

#Region "properties"
        Public Property expand_start_size() As Single
            Get
                Return expand_start_size_
            End Get
            Set(value As Single)
                expand_start_size_ = value
            End Set
        End Property
        Public Property max_expand_size() As Single
            Get
                Return max_expand_size_
            End Get
            Set(value As Single)
                max_expand_size_ = value
            End Set
        End Property
        Public Property start_location() As Vector3
            Get
                Return start_location_
            End Get
            Set(value As Vector3)
                start_location_ = value
            End Set
        End Property
        Public Property Scatter_factor() As Single
            Get
                Return Scatter_factor_
            End Get
            Set(value As Single)
                Scatter_factor_ = value
            End Set
        End Property
        Public Property Expand_speed() As Single
            Get
                Return expand_speed_
            End Get
            Set(value As Single)
                expand_speed_ = value
            End Set
        End Property
        Public Property Fade_Time() As Single
            Get
                Return fade_time_
            End Get
            Set(value As Single)
                fade_time_ = value
            End Set
        End Property
        Public Property update_time() As Single
            Get
                Return update_time_
            End Get
            Set(value As Single)
                update_time_ = value
            End Set
        End Property
        Public Property particle_count() As Integer
            Get
                Return particle_count_
            End Get
            Set(value As Integer)
                particle_count_ = value
            End Set
        End Property
        Public Property birth_speed() As Int64
            Get
                Return birth_speed_
            End Get
            Set(value As Int64)
                birth_speed_ = value
            End Set
        End Property
        Public Property continuous() As Boolean
            Get
                Return continuous_
            End Get
            Set(value As Boolean)
                continuous_ = value
            End Set
        End Property
        Public Property fixed_expand_speed() As Boolean
            Get
                Return Fixed_expand_speed_
            End Get
            Set(value As Boolean)
                Fixed_expand_speed_ = value
            End Set
        End Property
        Public Property image_atlas_id() As Integer
            Get
                Return image_atlas_id_
            End Get
            Set(value As Integer)
                image_atlas_id_ = value
            End Set
        End Property
        Public Property note() As String
            Get
                Return Note_
            End Get
            Set(value As String)
                Note_ = value
            End Set
        End Property
        Public Property total_frames() As Single
            Get
                Return total_frames_
            End Get
            Set(value As Single)
                total_frames_ = value
            End Set
        End Property
        Public Property row_length() As Single
            Get
                Return row_length_
            End Get
            Set(value As Single)
                row_length_ = value
            End Set
        End Property

#End Region

        Public Sub initialize()

            Dim Z_adjust As Single = 1.0F

            If particle_count_ = 0 Then
                Throw New Exception("Explosion_1 has not particle count!")
                Return
            End If
            ReDim particles(particle_count_)
            For i = 0 To particle_count_

                particles(i) = New particle_ ' new

                particles(i).row_length = row_length
                particles(i).total_frames = total_frames

                Dim v = get_random_vector3(expand_speed_) 'get a random vector in -0.5 to 0.5 range
                v.Y += 0.5F
                v.Normalize()
                particles(i).rotation_angle = (v.X / expand_speed_) * PI * 2 'random rotation angle
                particles(i).scatter_direction = v

                particles(i).location = start_location_
                particles(i).this_particle_time = 0

                If fixed_expand_speed Then
                    particles(i).expand_factor = expand_speed_
                Else
                    v = get_random_vector3(1.0)
                    particles(i).expand_factor = (v.X + 0.5 + expand_speed_) * Z_adjust
                End If
            Next

        End Sub
        Public Sub execute()

            If timer Is Nothing Then
                timer = New Stopwatch
            End If
            If Not timer.IsRunning Then
                timer.Start()
            End If

            'Give birth if it's due
            If timer.ElapsedMilliseconds > birth_speed + time_delta Then
                time_delta = timer.ElapsedMilliseconds
                For i = 0 To particle_count_ - 1
                    If Not particles(i).aLive Then
                        particles(i).aLive = True
                        Dim v = get_random_vector3(1.0)
                        particles(i).frame_index = (v.Y + 0.5) * 90

                        Exit For
                    End If
                Next
            End If

            'Update each particle
            For i = 0 To particle_count_
                If particles(i).aLive Then

                    'is it time to update this particle?
                    If timer.ElapsedMilliseconds >= particles(i).this_particle_time Then

                        timer.Stop()
                        'updata time
                        particles(i).this_particle_time = timer.ElapsedMilliseconds + update_time_

                        'update location using scatter
                        particles(i).location += particles(i).scatter_direction * Scatter_factor

                        'increment image index
                        particles(i).frame_index += 1.0F
                        If particles(i).frame_index >= total_frames Then

                            If Not continuous_ Then
                                particles(i).aLive = False
                                done_count += 1
                                If done_count = particle_count_ Then
                                    DONE = True ' signals this emitter can be reset or removed
                                    particles(i).aLive = False
                                End If
                            Else
                                particles(i).frame_index = 0
                                particles(i).expand_scale = expand_start_size
                                particles(i).location = start_location_
                            End If
                        End If

                        'update quad size
                        particles(i).expand_scale += particles(i).expand_factor
                        If particles(i).expand_scale >= max_expand_size_ Then
                            particles(i).expand_scale = max_expand_size_
                        End If

                        timer.Start()


                    End If

                End If
            Next


            explode_type_1_shader.Use()


            Explosion_11776x512_91tiles_256x256_ID.BindUnit(0)
            ALPHA_LUT_ID.BindUnit(1)

            For i = 0 To particle_count_ - 1
                If particles(i).aLive Then
                    Dim matrix = Matrix4.CreateTranslation(particles(i).location)

                    GL.Uniform1(explode_type_1_shader("row_length"), particles(i).row_length)
                    GL.Uniform1(explode_type_1_shader("total_frames"), particles(i).total_frames)

                    GL.UniformMatrix4(explode_type_1_shader("matrix"), False, matrix)
                    GL.Uniform1(explode_type_1_shader("frame_index"), particles(i).frame_index)
                    GL.Uniform1(explode_type_1_shader("rot_angle"), particles(i).rotation_angle)
                    GL.Uniform1(explode_type_1_shader("scale"), particles(i).expand_scale)

                    GL.BindVertexArray(defaultVao)
                    GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4)

                End If
            Next
            explode_type_1_shader.Use()
            unbind_textures(1)
        End Sub
        Private Sub draw_Particle(ByVal id As Integer)

        End Sub
    End Structure
    Public sort_lists() As sort_list_
    Public Structure sort_list_
        Public list() As Single
    End Structure



End Module
