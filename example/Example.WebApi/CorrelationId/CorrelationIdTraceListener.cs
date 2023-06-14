using System.Diagnostics;

namespace Example.WebApi.CorrelationId
{
    

    public class TestDiagnosticObserver : IObserver<DiagnosticListener>
    {
        public TestDiagnosticObserver()
        {
        }

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name.StartsWith("System.Net.Http."))
            {
                value.Subscribe(new TestKeyValueObserver());
            }
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    public class TestKeyValueObserver : IObserver<KeyValuePair<string, object?>>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
        }
    }
}
