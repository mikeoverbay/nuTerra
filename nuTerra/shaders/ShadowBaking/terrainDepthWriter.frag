#version 450 core

out vec4 color;

in vec4 v_position;
in vec2 uv;

void main(void){

    float scale = 3000.0;
    float d = v_position.z / v_position.w ;

        d = d* 0.5 + 0.5;
    
    float d2 = d * d;
   
    // Adjusting moments (this is sort of bias per pixel) using derivative
    float dx = dFdx(d);
    float dy = dFdy(d);
    d2 += 0.25*(dx*dx+dy*dy) ;  
    color = vec4( d * scale, d2 * scale, 0.0, 1.0 );

    if (uv.x < 0.005 || uv.x > 0.995 || uv.y < 0.005 || uv.y > 0.995)
    {
        color = vec4(0.0,0.0,0.0,0.0);
    }
}