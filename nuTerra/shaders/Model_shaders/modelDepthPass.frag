#version 450 core

out uint id;

flat in uint model_id;

void main(void){

	id = model_id;

}