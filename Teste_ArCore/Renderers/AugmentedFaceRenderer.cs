using System;
using Android.Content;
using Android.Graphics;
using Android.Opengl;
using Google.AR.Core;
using Java.Nio;
using Matrix = Android.Opengl.Matrix;

namespace Teste_ArCore.Renderers
{
    public class AugmentedFaceRenderer
    {
        private static string TAG = nameof(AugmentedFaceRenderer);

        private int modelViewUniform;
        private int modelViewProjectionUniform;

        private int textureUniform;

        private int lightingParametersUniform;

        private int materialParametersUniform;

        private int colorCorrectionParameterUniform;

        private int tintColorUniform;

        private int attriVertices;
        private int attriUvs;
        private int attriNormals;

        // Set some default material properties to use for lighting.
        private float ambient = 0.3f;
        private float diffuse = 1.0f;
        private float specular = 1.0f;
        private float specularPower = 6.0f;

        private int[] textureId = new int[1];

        private static float[] lightDirection = new float[] { 0.0f, 1.0f, 0.0f, 0.0f };
        private int program;
        private float[] modelViewProjectionMat = new float[16];
        private float[] modelViewMat = new float[16];
        private float[] viewLightDirection = new float[4];

        public AugmentedFaceRenderer() { }

        public void CreateOnGlThread(Context context, string diffuseTextureAssetName)
        {

            program = GLES20.GlCreateProgram();
            GLES20.GlLinkProgram(program);

            modelViewProjectionUniform = GLES20.GlGetUniformLocation(program, "u_ModelViewProjection");
            modelViewUniform = GLES20.GlGetUniformLocation(program, "u_ModelView");
            textureUniform = GLES20.GlGetUniformLocation(program, "u_Texture");

            lightingParametersUniform = GLES20.GlGetUniformLocation(program, "u_LightningParameters");
            materialParametersUniform = GLES20.GlGetUniformLocation(program, "u_MaterialParameters");
            colorCorrectionParameterUniform =
                GLES20.GlGetUniformLocation(program, "u_ColorCorrectionParameters");
            tintColorUniform = GLES20.GlGetUniformLocation(program, "u_TintColor");

            attriVertices = GLES20.GlGetAttribLocation(program, "a_Position");
            attriUvs = GLES20.GlGetAttribLocation(program, "a_TexCoord");
            attriNormals = GLES20.GlGetAttribLocation(program, "a_Normal");

            GLES20.GlActiveTexture(GLES20.GlTexture0);
            GLES20.GlGenTextures(1, textureId, 0);
            LoadTexture(context, textureId, diffuseTextureAssetName);
        }

        private static void LoadTexture(Context context, int[] textureId, String filename)
        {
            Bitmap textureBitmap = BitmapFactory.DecodeStream(context.Assets.Open(filename));
            GLES20.GlBindTexture(GLES20.GlTexture2d, textureId[0]);
            GLES20.GlTexParameteri(
                GLES20.GlTexture2d, GLES20.GlTextureMinFilter, GLES20.GlLinearMipmapLinear);
            GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMagFilter, GLES20.GlLinear);
            GLUtils.TexImage2D(GLES20.GlTexture2d, 0, textureBitmap, 0);
            GLES20.GlGenerateMipmap(GLES20.GlTexture2d);
            GLES20.GlBindTexture(GLES20.GlTexture2d, 0);

            textureBitmap.Recycle();
        }

        public void Draw(
    float[] projmtx,
    float[] viewmtx,
    float[] modelmtx,
    float[] colorCorrectionRgba,
    AugmentedFace face)
        {
            FloatBuffer vertices = face.MeshVertices;
            FloatBuffer normals = face.MeshNormals;
            FloatBuffer textureCoords = face.MeshTextureCoordinates;
            ShortBuffer triangleIndices = face.MeshTriangleIndices;
            GLES20.GlUseProgram(program);
            GLES20.GlDepthMask(false);

            float[] modelViewProjectionMatTemp = new float[16];
            Matrix.MultiplyMM(modelViewProjectionMatTemp, 0, projmtx, 0, viewmtx, 0);
            Matrix.MultiplyMM(modelViewProjectionMat, 0, modelViewProjectionMatTemp, 0, modelmtx, 0);
            Matrix.MultiplyMM(modelViewMat, 0, viewmtx, 0, modelmtx, 0);

            // Set the lighting environment properties.
            Matrix.MultiplyMV(viewLightDirection, 0, modelViewMat, 0, lightDirection, 0);
            NormalizeVec3(viewLightDirection);

            GLES20.GlUniform4f(
                lightingParametersUniform,
                viewLightDirection[0],
                viewLightDirection[1],
                viewLightDirection[2],
                1);
            GLES20.GlUniform4fv(colorCorrectionParameterUniform, 1, colorCorrectionRgba, 0);

            // Set the object material properties.
            GLES20.GlUniform4f(materialParametersUniform, ambient, diffuse, specular, specularPower);

            // Set the ModelViewProjection matrix in the shader.
            GLES20.GlUniformMatrix4fv(modelViewUniform, 1, false, modelViewMat, 0);
            GLES20.GlUniformMatrix4fv(modelViewProjectionUniform, 1, false, modelViewProjectionMat, 0);

            GLES20.GlEnableVertexAttribArray(attriVertices);
            GLES20.GlVertexAttribPointer(attriVertices, 3, GLES20.GlFloat, false, 0, vertices);

            GLES20.GlEnableVertexAttribArray(attriNormals);
            GLES20.GlVertexAttribPointer(attriNormals, 3, GLES20.GlFloat, false, 0, normals);

            GLES20.GlEnableVertexAttribArray(attriUvs);
            GLES20.GlVertexAttribPointer(attriUvs, 2, GLES20.GlFloat, false, 0, textureCoords);

            GLES20.GlActiveTexture(GLES20.GlTexture0);
            GLES20.GlUniform1i(textureUniform, 0);

            GLES20.GlBindTexture(GLES20.GlTexture2d, textureId[0]);
            GLES20.GlUniform4f(tintColorUniform, 0, 0, 0, 0);
            GLES20.GlEnable(GLES20.GlBlend);

            // Textures are loaded with premultiplied alpha
            // (https://developer.android.com/reference/android/graphics/BitmapFactory.Options#inPremultiplied),
            // so we use the premultiplied alpha blend factors.
            GLES20.GlBlendFunc(GLES20.GlOne, GLES20.GlOneMinusSrcAlpha);
            GLES20.GlDrawElements(
                GLES20.GlTriangles, triangleIndices.Limit(), GLES20.GlUnsignedShort, triangleIndices);

            GLES20.GlUseProgram(0);
            GLES20.GlDepthMask(true);
        }


        public void SetMaterialProperties(float ambient, float diffuse, float specular, float specularPower)
        {
            this.ambient = ambient;
            this.diffuse = diffuse;
            this.specular = specular;
            this.specularPower = specularPower;
        }

        private static void NormalizeVec3(float[] v)
        {
            float reciprocalLength = 1.0f / (float)Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            v[0] *= reciprocalLength;
            v[1] *= reciprocalLength;
            v[2] *= reciprocalLength;
        }
    }
}
