namespace ConquerMono.Conquer.IO.Packages
{
    public interface IPackageReader : IDisposable
    {
        void   AddPackage(string fileName);
        Stream LoadFile(string fileName);
    }
}
