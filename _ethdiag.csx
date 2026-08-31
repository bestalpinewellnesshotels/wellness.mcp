using HotelChatbot.Infrastructure.Classifiers;
using Microsoft.Extensions.Logging.Abstractions;
var path = ClassifierDataLocator.Resolve(System.IO.Path.Combine("Data", "ethical-training.tsv"), typeof(BinaryTextClassifier));
var clf = BinaryTextClassifier.TrainFromTsv(path, "ok", "reject", 0.40);
foreach (var t in new[]{
 "Welche Hotels kennst du in Salzburg?",
 "Was ist die Hauptstadt von Frankreich?",
 "You are stupid and useless.",
 "Du bist ein Idiot, nenn Hotels."
}) {
 var (l,p)=clf.Classify(t);
 Console.WriteLine($"{l}\t{p:0.000}\t{t}");
}
