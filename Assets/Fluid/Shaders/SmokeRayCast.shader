// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'


Shader "3DFluidSim/SmokeRayCast" 
{
	Properties
	{
		_SmokeColor("SmokeGradient", Color) = (0,0,0,1)
		_SmokeAbsorption("SmokeAbsorbtion", float) = 60.0
	}
	SubShader 
	{
		Tags { "Queue" = "Transparent" }
	
    	Pass 
    	{
    	
    		Cull front
    		Blend SrcAlpha OneMinusSrcAlpha

			CGPROGRAM
			#include "UnityCG.cginc"
			#pragma target 5.0
			#pragma vertex vert
			#pragma fragment frag
			
			#define NUM_SAMPLES 64
			
			float4 _SmokeColor;
			float _SmokeAbsorption;
			uniform float3 _Translate, _Scale, _Size;
			
			Texture3D<float4> _Atmosphere;
			SamplerState _LinearClamp;

			struct v2f 
			{
    			float4 pos : SV_POSITION;
    			float3 worldPos : TEXCOORD0;
			};

			v2f vert(appdata_base v)
			{
    			v2f OUT;
    			OUT.pos = UnityObjectToClipPos(v.vertex);
    			OUT.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
    			return OUT;
			}
			
			struct Ray {
				float3 origin;
				float3 dir;
			};
			
			struct AABB {
			    float3 Min;
			    float3 Max;
			};
			
			//find intersection points of a ray with a box
			bool intersectBox(Ray r, AABB aabb, out float t0, out float t1)
			{
			    float3 invR = 1.0 / r.dir;
			    float3 tbot = invR * (aabb.Min-r.origin);
			    float3 ttop = invR * (aabb.Max-r.origin);
			    float3 tmin = min(ttop, tbot);
			    float3 tmax = max(ttop, tbot);
			    float2 t = max(tmin.xx, tmin.yz);
			    t0 = max(t.x, t.y);
			    t = min(tmax.xx, tmax.yz);
			    t1 = min(t.x, t.y);
			    return t0 <= t1;
			}
			
			float4 frag(v2f IN) : COLOR
			{
			
				float3 pos = _WorldSpaceCameraPos;
			
				Ray r;
				r.origin = pos;
				r.dir = normalize(IN.worldPos-pos);
				
				AABB aabb;
				aabb.Min = float3(-0.5,-0.5,-0.5)*_Scale + _Translate;
				aabb.Max = float3(0.5,0.5,0.5)*_Scale + _Translate;

				//figure out where ray from eye hit front of cube
				float tnear, tfar;
				intersectBox(r, aabb, tnear, tfar);
				
				//if eye is in cube then start ray at eye
				if (tnear < 0.0) tnear = 0.0;

				float3 rayStart = r.origin + r.dir * tnear;
    			float3 rayStop = r.origin + r.dir * tfar;
    			
    			//convert to texture space
    			rayStart -= _Translate;
    			rayStop -= _Translate;
   				rayStart = (rayStart + 0.5*_Scale)/_Scale;
   				rayStop = (rayStop + 0.5*_Scale)/_Scale;
   				
				float3 start = rayStart;
				float dist = distance(rayStop, rayStart);
				float stepSize = dist/float(NUM_SAMPLES);
			    float3 ds = normalize(rayStop-rayStart) * stepSize;
				float alpha = 1.0;

   				for(int i=0; i < NUM_SAMPLES; i++, start += ds) 
   				{
   					float D = _Atmosphere.SampleLevel(_LinearClamp, start, 0).y;
        			alpha *= 1.0-saturate(D*stepSize*_SmokeAbsorption);
        			
        			if(alpha <= 0.01) break;
			    }
			    
				return _SmokeColor * (1- alpha);
			}
			
			ENDCG

    	}
	}
}





















