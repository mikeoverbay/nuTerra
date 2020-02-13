// based on http://rastergrid.com/blog/2010/02/instance-culling-using-geometry-shaders/
#version 430 core

layout(location = 6) in vec4 InstancePosition;

mat4 view;
mat4 projection;

uniform vec3 ObjectExtent;

out vec4 OrigPosition;
flat out int objectVisible;

void main(void) {

   OrigPosition = InstancePosition;
   
   /* calculate modelview projection matrix */
   mat4 MVP = projection * view;
   
   /* create the bounding box of the object */
   vec4 BoundingBox[8];
   BoundingBox[0] = MVP * ( InstancePosition + vec4( ObjectExtent.x, ObjectExtent.y, ObjectExtent.z, 1.0) );
   BoundingBox[1] = MVP * ( InstancePosition + vec4(-ObjectExtent.x, ObjectExtent.y, ObjectExtent.z, 1.0) );
   BoundingBox[2] = MVP * ( InstancePosition + vec4( ObjectExtent.x,-ObjectExtent.y, ObjectExtent.z, 1.0) );
   BoundingBox[3] = MVP * ( InstancePosition + vec4(-ObjectExtent.x,-ObjectExtent.y, ObjectExtent.z, 1.0) );
   BoundingBox[4] = MVP * ( InstancePosition + vec4( ObjectExtent.x, ObjectExtent.y,-ObjectExtent.z, 1.0) );
   BoundingBox[5] = MVP * ( InstancePosition + vec4(-ObjectExtent.x, ObjectExtent.y,-ObjectExtent.z, 1.0) );
   BoundingBox[6] = MVP * ( InstancePosition + vec4( ObjectExtent.x,-ObjectExtent.y,-ObjectExtent.z, 1.0) );
   BoundingBox[7] = MVP * ( InstancePosition + vec4(-ObjectExtent.x,-ObjectExtent.y,-ObjectExtent.z, 1.0) );
   
   /* check how the bounding box resides regarding to the view frustum */   
   int outOfBound[6] = int[6]( 0, 0, 0, 0, 0, 0 );

   for (int i=0; i<8; i++)
   {
      if ( BoundingBox[i].x >  BoundingBox[i].w ) outOfBound[0]++;
      if ( BoundingBox[i].x < -BoundingBox[i].w ) outOfBound[1]++;
      if ( BoundingBox[i].y >  BoundingBox[i].w ) outOfBound[2]++;
      if ( BoundingBox[i].y < -BoundingBox[i].w ) outOfBound[3]++;
      if ( BoundingBox[i].z >  BoundingBox[i].w ) outOfBound[4]++;
      if ( BoundingBox[i].z < -BoundingBox[i].w ) outOfBound[5]++;
   }

   bool inFrustum = true;
   
   for (int i=0; i<6; i++)
      if ( outOfBound[i] == 8 ) {
         inFrustum = false;
      }

   objectVisible = inFrustum ? 1 : 0;
}
