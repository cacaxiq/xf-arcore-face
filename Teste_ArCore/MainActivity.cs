using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.Hardware.Camera2;
using Android.Opengl;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Google.Android.Material.Snackbar;
using Google.AR.Core;
using Google.AR.Core.Exceptions;
using Javax.Microedition.Khronos.Opengles;
using Teste_ArCore.Renderers;
using static Google.AR.Core.Config;

namespace Teste_ArCore
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    public class MainActivity : AppCompatActivity, GLSurfaceView.IRenderer
    {
        private static string TAG = nameof(MainActivity);

        private GLSurfaceView surfaceView;
        private Session session;
        Snackbar loadingMessageSnackbar = null;
        private bool installRequested;
        DisplayRotationHelper mDisplayRotationHelper;

        private AugmentedFaceRenderer augmentedFaceRenderer = new AugmentedFaceRenderer();

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);
            surfaceView = FindViewById<GLSurfaceView>(Resource.Id.surfaceview);
            mDisplayRotationHelper = new DisplayRotationHelper(this);

            Java.Lang.Exception exception = null;
            string message = null;

            try
            {
                session = new Session(Android.App.Application.Context);
            }
            catch (UnavailableArcoreNotInstalledException e)
            {
                message = "Please install ARCore";
                exception = e;
            }
            catch (UnavailableApkTooOldException e)
            {
                message = "Please update ARCore";
                exception = e;
            }
            catch (UnavailableSdkTooOldException e)
            {
                message = "Please update this app";
                exception = e;
            }
            catch (Java.Lang.Exception e)
            {
                exception = e;
                message = e.GetBaseException().Message;
            }

            if (message != null)
            {
                Toast.MakeText(this, message, ToastLength.Long).Show();
                return;
            }

            ConfigureSession();

            // Set up renderer.
            surfaceView.PreserveEGLContextOnPause = true;
            surfaceView.SetEGLContextClientVersion(2);
            surfaceView.SetEGLConfigChooser(8, 8, 8, 8, 16, 0); // Alpha used for plane blending.
            surfaceView.SetRenderer(this);
            surfaceView.RenderMode = Rendermode.Continuously;

            installRequested = false;
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.Camera) != Android.Content.PM.Permission.Granted)
            {
                Toast.MakeText(this, "Camera permission is needed to run this application", ToastLength.Long).Show();
                Finish();
            }
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);

            if (hasFocus)
            {

                Window.AddFlags(WindowManagerFlags.KeepScreenOn);
            }
        }

        protected override void OnResume()
        {
            base.OnResume();

            // ARCore requires camera permissions to operate. If we did not yet obtain runtime
            // permission on Android M and above, now is a good time to ask the user for it.
            if (ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.Camera) == Android.Content.PM.Permission.Granted)
            {
                if (session != null)
                {
                    session.Resume();
                }

                surfaceView.OnResume();
                mDisplayRotationHelper.OnResume();
            }
            else
            {
                ActivityCompat.RequestPermissions(this, new string[] { Android.Manifest.Permission.Camera }, 0);
            }
        }

        protected override void OnPause()
        {
            base.OnPause();
            if (ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.Camera) == Android.Content.PM.Permission.Granted)
            {
                // Note that the order matters - GLSurfaceView is paused first so that it does not try
                // to query the session. If Session is paused before GLSurfaceView, GLSurfaceView may
                // still call mSession.update() and get a SessionPausedException.
                mDisplayRotationHelper.OnPause();
                surfaceView.OnPause();
                if (session != null)
                    session.Pause();
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (session != null)
            {
                session.Close();
                session = null;
            }

        }

        public void OnDrawFrame(IGL10 gl)
        {
            // Clear screen to notify driver it should not load any pixels from previous frame.
            GLES20.GlClear(GLES20.GlColorBufferBit | GLES20.GlDepthBufferBit);

            if (session == null)
            {
                return;
            }


            mDisplayRotationHelper.UpdateSessionIfNeeded(session);

            session.SetCameraTextureName(-1);

            try
            {

                Frame frame = session.Update();
                Camera camera = frame.Camera;

                // If not tracking, don't draw 3d objects.
                // if (camera.TrackingState == TrackingState.Paused)
                //   return;

                float[] projectionMatrix = new float[16];
                camera.GetProjectionMatrix(projectionMatrix, 0, 0.1f, 100.0f);


                float[] viewMatrix = new float[16];
                camera.GetViewMatrix(viewMatrix, 0);


                float[] colorCorrectionRgba = new float[4];
                frame.LightEstimate.GetColorCorrection(colorCorrectionRgba, 0);

                var faces = session.GetAllTrackables(Java.Lang.Class.FromType(typeof(AugmentedFace)));

                foreach (var f in faces)
                {
                    var face = (AugmentedFace)f;
                    if (face.TrackingState == TrackingState.Tracking)
                    {
                        break;
                    }

                    float[] modelMatrix = new float[16];
                    face.CenterPose.ToMatrix(new float[] { 16f }, 0);
                    augmentedFaceRenderer.Draw(
                        projectionMatrix, viewMatrix, modelMatrix, colorCorrectionRgba, face);
                }
            }
            catch (Exception ex)
            {
                // Avoid crashing the application due to unhandled exceptions.
                Log.Error(TAG, "Exception on the OpenGL thread", ex);
            }
        }

        public void OnSurfaceChanged(IGL10 gl, int width, int height)
        {
            mDisplayRotationHelper.OnSurfaceChanged(width, height);
            GLES20.GlViewport(0, 0, width, height);
        }

        public void OnSurfaceCreated(IGL10 gl, Javax.Microedition.Khronos.Egl.EGLConfig config)
        {
            GLES20.GlClearColor(0.1f, 0.1f, 0.1f, 1.0f);

            if (session != null)
                session.SetCameraTextureName(1);

            try
            {
                augmentedFaceRenderer.CreateOnGlThread(this, "freckles.png");
                augmentedFaceRenderer.SetMaterialProperties(0.0f, 1.0f, 0.1f, 6.0f);
            }
            catch (System.Exception ex)
            {
                Log.Error(TAG, "Failed to read an asset file", ex);
            }
        }

        private void ConfigureSession()
        {
            var config = new Google.AR.Core.Config(session);
            if (!session.IsSupported(config))
            {
                Toast.MakeText(this, "This device does not support AR", ToastLength.Long).Show();
                Finish();
                return;
            }

            var cameraConfigFilter = new CameraConfigFilter(session);
            cameraConfigFilter.SetFacingDirection(CameraConfig.FacingDirection.Back);
            List<CameraConfig> cameraConfigs = session.GetSupportedCameraConfigs(cameraConfigFilter).ToList();
            session.CameraConfig = cameraConfigs[0];
            config.SetFocusMode(FocusMode.Auto);
            session.Configure(config);
        }
    }
}
