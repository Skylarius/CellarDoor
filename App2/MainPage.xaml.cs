﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Media.SpeechRecognition;
using Windows.Media.SpeechSynthesis;
using Windows.Devices.Gpio;

namespace App2
{
    public sealed partial class MainPage : Page
    {
        private const String SUBSCRIPTION_FACE_DETECTION_CODE = ""; //The subscription code recived from Azure at subscription to face APIs moment
        private static MediaCapture _mediaCapture; //camera capturing handler obj
        private bool isDetecting = false, isCapturing = false; 
        int photoContainerIndexRow = 0, photoContainerIndexCol = 0; //index of row and column of actual free places in Grid of Users' Photos
        List<User> Users;
        User loggingUser, loggedUser; //loggingUser: user which is authenticate by face but not by password; loggedUser: user which is wholly authenticate, and can be served (can be allowed to open door) 
        string[] affermation = {"right", "yes", "ok", "perfect", "confirm"}; //some affermation words, actually used in Register() only.
        List<Tuple<string[], string>> additionalCommands; //some additional command to Register, Subscribe, Clear, Go Camera and affermation. Actually used just for fun
        GpioPin pin=null;
        private const int LED_PIN = 5;
        SpeechRecognizer sr; //speech recognizer handler obj
        SpeechSynthesizer ss; //speech synthetizer handler obj
        private Func<Task> Service; //procedure taken when user authenticated. Actually could be only one of ServicePinTurn and ServiceNull.

        public MainPage()
        {
            this.InitializeComponent();
            loggingUser = null;
            loggedUser = null;
            Users = new List<User>();
            Service = ServicePinNull; //default procedure
            InitializeSpeechSynthesizer();
            InitializeSpeechRecognizer();
            InitializeGPIO();
            InitializeUsers();
        }

        //GENERAL PURPOSE FUNCTION
        ///<summary>
        ///     Takes photo, saves photo file on temp folder, sets a place of grid as new photo and then returns user with attribute PhotoFace.
        ///</summary>
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
            Grid.SetRow(img, photoContainer.RowDefinitions.Count - 1);
            Grid.SetColumn(img, photoContainer.ColumnDefinitions.Count - 1);

            User u = new User();
            u.PhotoFace = img;
            await Print("Picture taken!");
            return u;
        }
        ///<summary>
        ///    Reads a file content and returns this serialized as bytes array.
        ///</summary>
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
        ///<summary>
        ///    Sets photo as body of HTTP request to APIs of Azure. Then waits for response and decapsulates corresponding faceId, which identificates the singular photo.
        ///</summary>
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
            try
            {
                faceId = faceId.Substring(faceId.IndexOf("\"faceId\":\"") + ("\"faceId\":\"").Length);
                faceId = faceId.Substring(0, faceId.IndexOf("\""));
            }
            catch(Exception e) {
                await Print("response=" + faceId);
                throw e;
            }
            return faceId;

        }
        ///<summary>
        ///    Sets logging user's faceId and list of users faceIds as content of HTTP request to APIs of Azure. Then waits for response and decapsulate a faceId, which
        ///    corresponds to the similar faceId in faceIds list to the logging user's faceId
        ///</summary>
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
        ///<summary>
        ///    Prints on screen and then speeches text
        ///</summary>
        public async Task Print(String x)
        {
            txtInfo.Text += x + "\n";
            await Talk(x);
            await Task.Delay(x.Length*80);
            txtInfo.Text += "☺";
        }
        ///<summary>
        ///    Speeches text.
        ///</summary>
        private async Task Talk(string message)
        {
            var stream = await ss.SynthesizeTextToStreamAsync(message);
            mediaElement.SetSource(stream, stream.ContentType);
            //mediaElement.Play();

        }
        ///<summary>
        ///    Clears screen previous messages.
        ///</summary>
        public void Clear() {
            txtInfo.Text = "";
        }
        ///<summary>
        ///    Resets all text fields and optional buttons
        ///</summary>
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
        ///<summary>
        ///    Saves userslist on file and moves photoFace files from temp to local folder
        ///</summary>
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
        ///<summary>
        ///    Saves userlist on file.
        ///</summary>
        private static async Task SaveUsersListOnly(string FileName, List<User> _Data)
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
        }
        ///<summary>
        ///    Load users from file into Users list
        ///</summary>
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

        //INITIALIZERS
        ///<summary>
        ///    Loads users from file MyUsers.dat and then check if their registration is older than one day. If no load photos into grid, else remove users from users list.
        ///</summary>
        private async void InitializeUsers()
        {
            try
            {
                Users = await Load("MyUsers.dat");

                //Load Photos of User on grid if not expired (older than 24h)
                foreach(User u in Users)
                {
                    if ((DateTime.Now - u.registrationDate).TotalHours > 24)
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
                await SaveUsersListOnly("MyUsers.dat", Users);
                if (Users.Count!=0)
                    await Print("Users loaded succesfully!");
            }
            catch(Exception ex) {
                await Print("No users list to load!");
            }
        }
        private async void InitializeSpeechRecognizer()
        {
            try
            {
                sr = new SpeechRecognizer();

                //These for using XML SRGS file
                //StorageFile grammarContentFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///GrammarStructure.xml"));
                //SpeechRecognitionGrammarFileConstraint grammarConstraint = new SpeechRecognitionGrammarFileConstraint(grammarContentFile);
                //sr.Constraints.Add(grammarConstraint);

                await sr.CompileConstraintsAsync();
                await Print("Vocal recognizer initialized!");
                srController();
                await InitializeAdditionalCommands("ms-appx:///AdditionalCommands.txt");

            }
            catch(Exception e)
            {
                Print(e.ToString());
            }
        }

        private async void InitializeSpeechSynthesizer()
        {            
            ss = new SpeechSynthesizer();
            mediaElement.AutoPlay = true;
        }
        ///<summary>
        ///    Some fun additional commands are in a txt file. Check for AttitionalCommands.txt on project folder to understand grammar.
        ///</summary>
        private async Task InitializeAdditionalCommands(string filePath)
        {
            //ms - appx:///
            additionalCommands = new List<Tuple<string[], string>>();
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(filePath));
            foreach (string line in await FileIO.ReadLinesAsync(file))
            {
                string[] couple = line.Split("->");
                string[] commands = couple[0].Split('|');
                var tuple = new Tuple<string[], string>(commands, couple[1]);
                additionalCommands.Add(tuple);
            }
        }
        ///<summary>
        ///    If GPIO is attached, service becomes ServicePinTurn (see below)
        ///</summary>
        private async void InitializeGPIO()
        {
            var gpio = GpioController.GetDefault();

            // Show an error if there is no GPIO controller
            if (gpio == null)
            {
                pin = null;
                await Print("No GPIO controller.");
                return;
            }

            pin = gpio.OpenPin(LED_PIN);
            pin.Write(GpioPinValue.High);
            pin.SetDriveMode(GpioPinDriveMode.Output);

            await Print("GPIO inizialized.");
            Service = ServicePinTurn;
        }

        //BUTTONS & PROCEDURES
        
        private async void btnCamera_Click(object sender, RoutedEventArgs e)
        {
            Camera();    
        }
        ///<summary>
        ///    Activates capturing
        ///</summary>
        private async Task Camera()
        {
            if (isCapturing) return;
            isCapturing = true;
            _mediaCapture = new MediaCapture();
            await _mediaCapture.InitializeAsync();
            cePreview.Source = _mediaCapture;
            await _mediaCapture.StartPreviewAsync();
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            Clear();
        }
        private async void btnRegister_Click(object sender, RoutedEventArgs e)
        {
            await Register();
        }

        ///<summary>
        ///    Registers User. Get name, surname and password, checks for collision, save on application folder new list of users and
        ///    show photo on grid below. Registered photo is also sent to face detection server by api, which returns a faceId used for recognition.
        ///</summary>
        private async Task Register()
        {
            try
            {
                if (isCapturing == false) throw new Exception("Need to start Capturing");
                if (isDetecting == true) throw new Exception("Detection is already running!");
                isDetecting = true;
                User u = new User(txtSetNameSurname.Text.Split(' ')[0].ToUpper(), txtSetNameSurname.Text.Split(' ')[1].ToUpper(), txtSetPassword.Text);
                if (Users.Contains(u))
                {
                    await Print(u.Name.ToUpper() + ' ' + u.Surname.ToUpper() + " already subscribed. Do you want to replace existing information?");
                    btnOverrideName.Visibility = Visibility.Visible;
                    return;
                }

                User tempPhoto = await TakePhoto(u.Name + u.Surname);
                u.PhotoFace = tempPhoto.PhotoFace;

                await Print("Starting Subscription...");
                u.FaceId = await MakeRequestFaceDetect(await ApplicationData.Current.TemporaryFolder.GetFileAsync(u.Name + u.Surname));
                u.registrationDate = DateTime.Now;
                Users.Add(u);
                Save("MyUsers.dat", Users);
                await Print(u.Name.ToUpper() + " " + u.Surname.ToUpper() + " succesfully subscribed!");
                ResetAll();

                Grid.SetRow(tempPhoto.PhotoFace, photoContainerIndexRow);
                Grid.SetColumn(tempPhoto.PhotoFace, photoContainerIndexCol);
                var txt = new TextBlock();
                txt.HorizontalAlignment = HorizontalAlignment.Center;
                txt.VerticalAlignment = VerticalAlignment.Bottom;
                txt.Text = u.Name.Substring(0, 1).ToUpper() + ". " + u.Surname.ToUpper();
                photoContainer.Children.Add(txt);
                Grid.SetRow(txt, photoContainerIndexRow);
                Grid.SetColumn(txt, photoContainerIndexCol);
                photoContainerIndexCol = (++photoContainerIndexCol) % (photoContainer.ColumnDefinitions.Count-1);
                if (photoContainerIndexCol == 0) photoContainerIndexRow = ++photoContainerIndexRow % photoContainer.RowDefinitions.Count;
            }
            catch (Exception ex)
            {
                await Print(ex.Message);
                ResetAll();
            }
            finally
            {
                isDetecting = false;
            }
        }

        private async void btnOverrideName_Click(object sender, RoutedEventArgs e)
        {
            await OverrideName();
        }
        ///<summary>
        ///     Removes the users which name is in NameSurname text field, then Register new user.
        ///</summary>
        private async Task OverrideName()
        {
            try
            {
                User u = new User(txtSetNameSurname.Text.Split(' ')[0].ToUpper(), txtSetNameSurname.Text.Split(' ')[1].ToUpper(), txtSetPassword.Text.ToUpper());
                if (Users.Remove(u))
                    await Print("Replaced!");
                else throw new Exception("User not replaced");
                await Register();
            }
            catch (Exception ex) { await Print(ex.Message); }
            finally
            {
                ResetAll();
                isDetecting = false;
            }
        }

        private async void btnAuthenticate_Click(object sender, RoutedEventArgs e)
        {
            await Authenticate();
        }

        ///<summary>
        ///    Authenticate user and ask for password. Takes a picture and send it to face detection server, waits faceId, then
        ///    send faceId to face recognition server which returns faceIds of similar faces. Then match stored users with same faceId.
        ///</summary>

        private async Task Authenticate()
        {
            try
            {
                if (isCapturing == false) throw new Exception("Need to start Capturing");
                if (isDetecting == true) throw new Exception("Detection is already running!");
                isDetecting = true;
                User tempPhoto = await TakePhoto("temp");
                await Print("Starting Authentication...");
                tempPhoto.FaceId = await MakeRequestFaceDetect(await ApplicationData.Current.TemporaryFolder.GetFileAsync("temp"));

                await Print("Searching for corresponding face...");
                List<String> faceIds = new List<string>();
                foreach (User u in Users) faceIds.Add(u.FaceId);
                List<String> foundFaceIds = await MakeRequestFindSimilar(tempPhoto.FaceId, faceIds);

                await Print("Matching User...");
                User targetUser = null;
                foreach (User u in Users)
                    foreach (String foundFaceId in foundFaceIds)
                        if (u.FaceId == foundFaceId)
                        {
                            targetUser = u;
                            break;
                        }
                if (targetUser == null) await Print("Access Denied! Not recognized or suspicious-face user!");
                else
                {
                    loggingUser = targetUser;
                    int indexUser = Users.IndexOf(loggingUser);
                    rectFocus.Visibility = Visibility.Visible;
                    Grid.SetRow(rectFocus, indexUser / (photoContainer.ColumnDefinitions.Count - 1));
                    Grid.SetColumn(rectFocus, indexUser % (photoContainer.ColumnDefinitions.Count - 1));

                    await Print("Welcome " + targetUser.Name.ToUpper() + " " + targetUser.Surname.ToUpper() + ". Insert password to authenticate!");
                    labelSubmitPassword.Visibility = txtSubmitPassword.Visibility = btnSubmitPassword.Visibility = Visibility.Visible;

                }
            }
            catch (Exception ex)
            {
                await Print(ex.Message);
                ResetAll();
            }
            finally
            {
                isDetecting = false;
            }
        }

        private async void btnSubmitPassword_Click(object sender, RoutedEventArgs e) {
            await SubmitPassword();
        }
        ///<summary>
        ///    Submits password then, if matching, starts Service()
        ///</summary>

        private async Task SubmitPassword()
        {
            try
            {
                string password = txtSubmitPassword.Text.ToUpper();
                if (password != loggingUser.Password)
                {
                    await Print("Wrong password! Failed Authentication! Authenticate all over again!");
                    return;
                }
                //recognized!
                loggedUser = loggingUser;
                loggingUser = null;
                await Print("Perfect! You've been recognized " + loggedUser.Name.ToUpper() + " " + loggedUser.Surname.ToUpper() + "! You can now access my door!");
                await Service();
                loggedUser = loggingUser = null;
            }
            finally
            {
                ResetAll();
                loggedUser = loggingUser = null;
                rectFocus.Visibility = Visibility.Collapsed;
            }
        }

        //SPEECH RECOGNITION
        ///<summary>
        ///    Main of Speech recognition.
        ///</summary>
        private async void srController()
        {
            await Print("Say the words on buttons to command!");
            bool done = false;
            while(true)
            {
                try
                {
                    SpeechRecognitionResult speechRecognitionResult;
                    do
                    {
                        speechRecognitionResult = await sr.RecognizeAsync();
                    } while (speechRecognitionResult.Text == "");
                    Clear();
                    await Print(speechRecognitionResult.Text.ToUpper());
                    foreach(string command in speechRecognitionResult.Text.ToLower().Split(' '))
                    {
                        switch(command)
                        {
                            case "authenticate":
                            case "recognize":
                                await Authenticate();
                                do
                                {
                                    speechRecognitionResult = await sr.RecognizeAsync();
                                } while (speechRecognitionResult.Text == "");
                                txtSubmitPassword.Text = speechRecognitionResult.Text.ToUpper();
                                await SubmitPassword();
                                done = true;
                                break;
                            case "subscribe":
                            case "register":
                                await Print("Say your first name please:");
                                do
                                {
                                    speechRecognitionResult = await sr.RecognizeAsync();
                                } while (speechRecognitionResult.Text == "");
                                txtSetNameSurname.Text = speechRecognitionResult.Text.ToUpper();
                                await Print("Say your surname please:");
                                do
                                {
                                    speechRecognitionResult = await sr.RecognizeAsync();
                                } while (speechRecognitionResult.Text == "");
                                txtSetNameSurname.Text += " " + speechRecognitionResult.Text.ToUpper();
                                await Print("Say the password please:");
                                do
                                {
                                    speechRecognitionResult = await sr.RecognizeAsync();
                                } while (speechRecognitionResult.Text == "");
                                txtSetPassword.Text = speechRecognitionResult.Text.ToUpper();
                                Clear();
                                await Print("Do you wish to confirm?");
                                do
                                {
                                    speechRecognitionResult = await sr.RecognizeAsync();
                                } while (speechRecognitionResult.Text == "");
                                if (affermation.Contains(speechRecognitionResult.Text.ToLower()))
                                    await Register();
                                else await Print("Subscribtion has been canceled!");
                                done = true;
                                break;
                            case "replace":
                            case "overwrite":
                                await OverrideName();
                                done = true;
                                break;
                            case "go":
                            case "camera":
                                btnCamera_Click(null, null);
                                done = true;
                                break;
                            case "clear":
                                Clear();
                                done = true;
                                break;
                            default:
                                foreach(Tuple<string[], string> t in additionalCommands)
                                {
                                    if (t.Item1.Contains(command))
                                    {
                                        await Print(t.Item2);
                                        done = true;
                                        break;
                                    }
                                }
                                break;

                        }
                        if (done)
                        {
                            break;
                        }
                    }
                }catch (Exception e) { await Print(e.Message); }
                finally { done = false; }
            }
        }


        //SERVICES
        ///<summary>
        ///    Turns LED on, waits 10 seconds, then turns it off.
        ///</summary>
        private async Task ServicePinTurn() {
            pin.Write(GpioPinValue.Low);
            await Task.Delay(10000);
            pin.Write(GpioPinValue.High);
        }

        private async Task ServicePinNull() { }
    }
}
