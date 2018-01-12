# CellarDoor
Software made for Windows IoT Core, where you use the camera to get facial recognition and vocal command to open a door.

Scope: the user come close to the webcam and wants to be authenticated to open a door/do something. So a photo of his face is taken, then matched to a user of a stored list of users. If matching is all-right the app ask user for password and he says it. If okay user is logged and service can be executed. Obviously user must subscribe to this service first.

This is a Universal Windows Platform (UWP) and has been made for a University project in Università di Catania. The aim is to use Azure APIs for facial recognition. I also added speech synthetizer and recognizer. The aimed device was Raspberry Pie 2, so you will find a GPIO function, but as a UWP you can run it on any Windows OS.
To setup this project first it's necessary to subscribe to Azure and to get a key, which can be pasted to proper variable in project. If you want to run it on PC in spite of Raspberry you have to compile with x86 device option.

Starting app as user, you fill find some buttons: Go Camera, Subscribe, Authenticate, Clear; other buttons will appear later. You can click on buttons or say the words on their labels to start procedures
Saying/Clicking "Go Camera" you start capturing from your WebCam. If on PC you may be asked for permission.
Saying "Subscribe" you will be asked for saying name, surname and password. Then you will have to confirm for typed/dictated choises. If okay a photo will be taken of your face and sent to Azure to subscribe. This is made by waiting a response containing a face identificator, that identificates the photo, that must be stored and matched to subscribed user. If user already exist this can be overwritten.
Sayng "Authenticate" a photo of your face will be immediately taken, then, like before, the photo will be sent to Azure waiting for a face identificator in the response; when face identificator has arrived, this, with list of users, is sent to Azure by another API, which wait for face identificator from list of ids which is the most similar to given id. If evrithing is all right user has been recognized and asked for password as additional assurance. Finally, if correct, service will be executed.
