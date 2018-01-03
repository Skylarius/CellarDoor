using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Core;
using Windows.Media.FaceAnalysis;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;


// Il modello di elemento Pagina vuota è documentato all'indirizzo https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x410

namespace App2
{
    /// <summary>
    /// Pagina vuota che può essere usata autonomamente oppure per l'esplorazione all'interno di un frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const String SUBSCRIPTION_FACE_DETECTION_CODE = "6b8f79b3b76d4efeb7c490dcd3f4f573";
        private FaceDetectionEffect _faceDetectionEffect;
        private static MediaCapture _mediaCapture;
        private IMediaEncodingProperties _previewProperties;
        private bool isDetecting = false, isCapturing = false;
        int photoContainerIndexRow = 0, photoContainerIndexCol = 0;
        List<User> Users;
        User loggingUser, loggedUser;
        
        public MainPage()
        {
            this.InitializeComponent();
            loggingUser = null;
            loggedUser = null;
            Users = new List<User>();
            InizializeUsers();
        }
        private async Task<User> TakePhoto(String filename)
        {
            var photoFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(filename, CreationCollisionOption.ReplaceExisting);
            await _mediaCapture.CapturePhotoToStorageFileAsync(ImageEncodingProperties.CreateBmp(), photoFile);

            IRandomAccessStream photoStream = await photoFile.OpenReadAsync();
            BitmapImage bitmap = new BitmapImage();
            bitmap.SetSource(photoStream);
            Image img = new Image() { Width = 100, Source = bitmap };

            //Add photo to grid

            photoContainer.Children.Add(img);
            Grid.SetRow(img, photoContainerIndexRow);
            Grid.SetColumn(img, photoContainerIndexCol);
            photoContainerIndexCol = (++photoContainerIndexCol) % photoContainer.ColumnDefinitions.Count;
            if (photoContainerIndexCol == 0) photoContainerIndexRow = ++photoContainerIndexRow % photoContainer.RowDefinitions.Count;

            User u = new User();
            u.PhotoFace = img;
            Print("Photo Captured!");
            return u;
        }
        public async Task<byte[]> ReadFile(StorageFile file)
        {
            byte[] fileBytes = null;
            using (IRandomAccessStreamWithContentType stream = await file.OpenReadAsync())
            {
                fileBytes = new byte[stream.Size];
                using (DataReader reader = new DataReader(stream))
                {
                    await reader.LoadAsync((uint)stream.Size);
                    reader.ReadBytes(fileBytes);
                }
            }

            return fileBytes;
        }
        async Task<String> MakeRequestFaceDetect(StorageFile photoFile)
        {
            var client = new HttpClient();

            // Request headers
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", SUBSCRIPTION_FACE_DETECTION_CODE);

            var uri = "https://westcentralus.api.cognitive.microsoft.com/face/v1.0/detect?";

            HttpResponseMessage response;

            // Request body
            byte[] byteData = await ReadFile(photoFile);
            using (var content = new ByteArrayContent(byteData))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                response = await client.PostAsync(uri, content);
            }
            if (response.IsSuccessStatusCode == false) throw new Exception("FAILED DETECTION RESPONSE");
            String faceId;
            faceId = await response.Content.ReadAsStringAsync();
            faceId = faceId.Substring(faceId.IndexOf("\"faceId\":\"") + ("\"faceId\":\"").Length);
            faceId = faceId.Substring(0, faceId.IndexOf("\""));
            return faceId;

        }
        async Task<List<String>> MakeRequestFindSimilar(String faceIdTarget, List<String> faceIds)
        {            
            var client = new HttpClient();

            // Request headers
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", SUBSCRIPTION_FACE_DETECTION_CODE);
            String faceIdsToString = "";
            foreach(String s in faceIds)
            {
                faceIdsToString += "\"" + s + "\",";
            }
            var uri = "https://westcentralus.api.cognitive.microsoft.com/face/v1.0/findsimilars?";

            HttpResponseMessage response;

            // Request body
            byte[] byteData = Encoding.UTF8.GetBytes("" +
                "{" +
                "\"faceId\":\"" + faceIdTarget + "\"," +
                "\"faceIds\":[" + faceIdsToString +  "]" +
                "}"
                );

            using (var content = new ByteArrayContent(byteData))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                response = await client.PostAsync(uri, content);
            }

            if (response.IsSuccessStatusCode == false) throw new Exception("FAILED FIND SIMILAR RESPONSE");
            String faceIdFound;
            faceIdFound = await response.Content.ReadAsStringAsync();

            List<String> faceIdsFound = new List<string>();
            while (faceIdFound.IndexOf("\"faceId\":\"")>0)
            {
                faceIdFound = faceIdFound.Substring(faceIdFound.IndexOf("\"faceId\":\"") + ("\"faceId\":\"").Length);
                faceIdsFound.Add(faceIdFound.Substring(0, faceIdFound.IndexOf("\"")));
            }
            return faceIdsFound;

        }
        public void Print(String x)
        {
            txtInfo.Text += x + "\n";
        }

        public void Clear() {
            txtInfo.Text = "";
        }
        public void ResetAll()
        {
            labelSubmitPassword.Visibility =
            txtSubmitPassword.Visibility =
            btnSubmitPassword.Visibility =
            btnOverrideName.Visibility = Visibility.Collapsed;

            txtSetPassword.Text =
            txtSetNameSurname.Text =
            txtSubmitPassword.Text = "";
        }
        private static async void Save(string FileName, List<User> _Data)
        {
            MemoryStream _MemoryStream = new MemoryStream();
            DataContractSerializer Serializer = new DataContractSerializer(typeof(List<User>));
            Serializer.WriteObject(_MemoryStream, _Data);

            Task.WaitAll();

            StorageFile _File = await ApplicationData.Current.LocalFolder.CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);

            using (Stream fileStream = await _File.OpenStreamForWriteAsync())
            {
                _MemoryStream.Seek(0, SeekOrigin.Begin);
                await _MemoryStream.CopyToAsync(fileStream);
                await fileStream.FlushAsync();
                fileStream.Dispose();
            }

            //Save Last Photo
            User u = _Data[_Data.Count -1];
            var tempPhotoFile = await ApplicationData.Current.TemporaryFolder.GetFileAsync(u.Name+u.Surname);
            var photoFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(u.Name+u.Surname, CreationCollisionOption.ReplaceExisting);
            await tempPhotoFile.MoveAndReplaceAsync(photoFile);
            
        }

        private async Task<List<User>> Load(string FileName)
        {
            StorageFolder _Folder = ApplicationData.Current.LocalFolder;
            StorageFile _File;
            List<User> Result;
   
            Task.WaitAll();
            _File = await _Folder.GetFileAsync(FileName);

            using (Stream stream = await _File.OpenStreamForReadAsync())
            {
                DataContractSerializer Serializer = new DataContractSerializer(typeof(List<User>));

                Result = (List<User>)Serializer.ReadObject(stream);

            }

            //Photo Load
            foreach(User u in Result)
            {
                var photoFile = await ApplicationData.Current.LocalFolder.GetFileAsync(u.Name + u.Surname);
                IRandomAccessStream photoStream = await photoFile.OpenReadAsync();
                BitmapImage bitmap = new BitmapImage();
                bitmap.SetSource(photoStream);
                u.PhotoFace = new Image() { Width = 100, Source = bitmap };
            }
            return Result;
        }

        private async void InizializeUsers()
        {
            try
            {
                Users = await Load("MyUsers.dat");

                //Load Photos of User on grid if not expired (older than 24h)
                foreach(User u in Users)
                {
                    if ((u.registrationDate - DateTime.Now).TotalHours > 24)
                    {
                        var photoFile = await ApplicationData.Current.LocalFolder.GetFileAsync(u.Name + u.Surname);
                        await photoFile.DeleteAsync();
                        Users.Remove(u);
                    }
                    else
                    {
                        photoContainer.Children.Add(u.PhotoFace);
                        Grid.SetRow(u.PhotoFace, photoContainerIndexRow);
                        Grid.SetColumn(u.PhotoFace, photoContainerIndexCol);

                        var txt = new TextBlock();
                        txt.HorizontalAlignment = HorizontalAlignment.Center;
                        txt.VerticalAlignment = VerticalAlignment.Bottom;
                        txt.Text = u.Name.Substring(0, 1).ToUpper() + ". " + u.Surname.ToUpper();
                        photoContainer.Children.Add(txt);
                        Grid.SetRow(txt, photoContainerIndexRow);
                        Grid.SetColumn(txt, photoContainerIndexCol);

                        photoContainerIndexCol = (++photoContainerIndexCol) % photoContainer.ColumnDefinitions.Count;
                        if (photoContainerIndexCol == 0) photoContainerIndexRow = ++photoContainerIndexRow % photoContainer.RowDefinitions.Count;
                    }
                }
                if (Users.Count!=0)
                    Print("Users succesfully loaded");
            }
            catch(Exception ex) {
                Print("No Users List to Load or");
            }
        }


        private async void btnCamera_Click(object sender, RoutedEventArgs e)
        {
            if (isCapturing) return;
            isCapturing = true;
            _mediaCapture = new MediaCapture();
            await _mediaCapture.InitializeAsync();
            cePreview.Source = _mediaCapture;
            await _mediaCapture.StartPreviewAsync();
        }

        ///<summary>
        ///    Take a picture, save it on Temp folder and set photoFace and photoFile of returned User u
        ///</summary>

        private async void btnRegister_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (isCapturing == false) throw new Exception("Need to start Capturing");
                if (isDetecting == true) throw new Exception("Detection is already running!");
                isDetecting = true;
                User u = new User(txtSetNameSurname.Text.Split(' ')[0].ToUpper(), txtSetNameSurname.Text.Split(' ')[1].ToUpper(), txtSetPassword.Text);
                if (Users.Contains(u))
                {
                    Print(u.Name.ToUpper() + ' ' + u.Surname.ToUpper() + "already registered. Do you want to override present info?");
                    btnOverrideName.Visibility = Visibility.Visible;
                    return;
                }

                User tempPhoto = await TakePhoto(u.Name+u.Surname);
                u.PhotoFace = tempPhoto.PhotoFace;

                Print("Beginning Registration...");
                u.FaceId = await MakeRequestFaceDetect(await ApplicationData.Current.TemporaryFolder.GetFileAsync(u.Name+u.Surname));

                Users.Add(u);
                Save("MyUsers.dat", Users);
                Print(u.Name.ToUpper() + " " + u.Surname.ToUpper() + " succesfully registered!!!");
                ResetAll();
            }
            catch (Exception ex) {
                Print(ex.ToString());
                ResetAll();
            }
            finally {
                isDetecting = false;
            }
        }

        private void btnOverrideName_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                User u = new User(txtSetNameSurname.Text.Split(' ')[0], txtSetNameSurname.Text.Split(' ')[1], txtSetPassword.Text);
                if (Users.Remove(u))
                    Print("Overridden!");
                else throw new Exception("User not Overridden");
                btnRegister_Click(sender, e);
            }
            catch (Exception ex){ Print(ex.ToString()); }
            finally
            {
                ResetAll();
                isDetecting = false;
            }
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            Clear();
        }

        private async void btnAuthenticate_Click(object sender, RoutedEventArgs e)
        {
            try {
                if (isCapturing == false) throw new Exception("Need to start Capturing");
                if (isDetecting == true) throw new Exception("Detection is already running!");
                isDetecting = true;
                User tempPhoto = await TakePhoto("temp");
                Print("Beginning of Authentication...");
                tempPhoto.FaceId = await MakeRequestFaceDetect(await ApplicationData.Current.TemporaryFolder.GetFileAsync("temp"));

                Print("Finding Corrispective...");
                List<String> faceIds = new List<string>();
                foreach (User u in Users) faceIds.Add(u.FaceId);
                List<String> foundFaceIds = await MakeRequestFindSimilar(tempPhoto.FaceId, faceIds);

                Print("Matching to User...");
                User target=null;
                foreach (User u in Users)
                    foreach (String foundFaceId in foundFaceIds)
                        if (u.FaceId == foundFaceId)
                        {
                            target = u;
                            break;
                        }
                if (target == null) Print("Access Denied: User not recognized or registered!");
                else {
                    loggingUser = target;
                    Print("Welcome " + target.Name.ToUpper() + " " + target.Surname.ToUpper() + ". Please Type Password to Authenticate!!!");
                    labelSubmitPassword.Visibility = txtSubmitPassword.Visibility = btnSubmitPassword.Visibility = Visibility.Visible;

                }
            }
            catch(Exception ex) {
                Print(ex.ToString());
                ResetAll();
            }
            finally
            {
                isDetecting = false;
            }
        }

        private void btnSubmitPassword_Click(object sender, RoutedEventArgs e) {
            try
            {
                string password = txtSubmitPassword.Text;
                if (password!=loggingUser.Password)
                {
                    Print("Wrong Password. Do Authentication all over again!");
                    return;
                }
                //recognized!
                loggedUser = loggingUser;
                loggingUser = null;
                Print("Great! You are authenticated " + loggedUser.Name.ToUpper() + " " + loggedUser.Surname.ToUpper() + "! Now you can be served!");
                Service();
                loggedUser = loggingUser = null;
            }
            finally
            {
                ResetAll();
                loggedUser = loggingUser = null;
            }
        }

        //SERVICES

        private void Service() { }
    }
}
