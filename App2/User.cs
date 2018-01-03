using System;
using Windows.Storage;
using Windows.UI.Xaml.Controls;
using System.Runtime.Serialization;

namespace App2
{
    [DataContract]
    class User : IEquatable<User>
    {
        [DataMember]
        public String Name { get; set; }
        [DataMember]
        public String Surname { get; set; }
        [DataMember]
        public String Password { get; set; }
        ///<summary>
        ///    Face Identificator obtained by API (https://westcentralus.api.cognitive.microsoft.com/face/v1.0/)
        ///</summary>
        [DataMember]
        public String FaceId { get; set; }
        [DataMember]
        public DateTime registrationDate { get; set; }
        ///<summary>
        ///    a 100x100 photo of User's face
        ///</summary>
        public Image PhotoFace { get; set; }
        public User() {
            Name = Surname = Password = FaceId = "NA";
        }

        public User(String name, String surname, String password) : this()
        {
            this.Name = name;
            this.Surname = surname;
            this.Password = password;
        }
        
        //Overriding Equals for Contains function
        public bool Equals(User u)
        {
            return ((u.Name == this.Name) && (u.Surname == this.Surname));
        }

    }
}
