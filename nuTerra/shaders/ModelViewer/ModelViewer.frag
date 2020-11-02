#version 450 core

out vec4 gColor;

uniform vec3 lightColor;
uniform sampler2D colorMap;
uniform vec3 viewPos;

in vec3 N;
in vec2 UV;
in vec3 FragPos;

void main(void)
{
    vec3 texColor =  texture(colorMap, UV).rgb;

    vec3 lightPosition = vec3 (30.0,30.0,30.0);

    float ambientStrength = 0.1;
    vec3 ambient = ambientStrength * lightColor * texColor;


    vec3 norm = normalize(N);
    vec3 lightDir = normalize(lightPosition - FragPos);

    float diff = max(dot(norm, lightDir), 0.0);
    vec3 diffuse = diff * lightColor * texColor;

    vec3 viewDir = normalize(viewPos - FragPos);
    vec3 reflectDir = reflect(-lightDir, norm);

    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32);
    vec3 specular =  spec * lightColor;
    vec3 result = (ambient + diffuse + specular);

	gColor.rgb = result.rgb;
	gColor.a = 1.0;

}