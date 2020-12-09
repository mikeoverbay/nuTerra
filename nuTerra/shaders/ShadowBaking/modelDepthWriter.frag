#version 450 core

out vec4 color;

in vec4 v_position;

void main(void){

    float scale = 5000.0;
    float d = v_position.z / v_position.w ;

        d = d* 0.5 + 0.5;
    
    float d2 = d * d;
   
    // Adjusting moments (this is sort of bias per pixel) using derivative
    float dx = dFdx(d);
    float dy = dFdy(d);
    d2 += 0.25*(dx*dx+dy*dy) ;  
    color = vec4( d * scale, d2 * scale, 0.0, 1.0 );

}