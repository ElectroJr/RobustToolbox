varying highp vec2 occlusion;

void main()
{
    // Blend-mode is subtraction. I.e., we always reduce visibility by some amount
    highp float occluded = 1.0 - occlusion.x/max(0.01,occlusion.y);
    occluded = clamp(occluded, 0.0, 1.0);
    gl_FragColor = vec4(occluded);
}


fuck me I should just do waht they do here

https://slembcke.github.io/SuperFastSoftShadows
https://slembcke.github.io/SuperFastHardShadows

in particular:
Infinite Homogeneous Coordinates

and then also re-do antumbra.
though I think antumba means we NEED backface culling to be disabled.


also
why am I drawing to a shadow map again? instead of just directly drawing to the light map?