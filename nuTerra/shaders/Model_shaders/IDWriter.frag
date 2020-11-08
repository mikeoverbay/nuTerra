#version 450 core
// Can't get it simpler :)

layout (location = 0) out uint id;

in VS_OUT {

 flat uint modelId;

} fs_in;

void main(void){

id = fs_in.modelId;

}