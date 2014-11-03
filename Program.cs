using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PausableObservable
{
    class Program
    {
        static void Main(string[] args)
        {
            Subject<bool> manager = new Subject<bool>();
            Subject<int> items = new Subject<int>();
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;

            items.Pausable(manager).Subscribe(next => Console.WriteLine("received {0}", next));

            Task.Run(async () =>
            {

                int i = 0;
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);

                    items.OnNext(i);

                    i++;
                }
            }, token);


            string command = null;
            while (!string.Equals(command = Console.ReadLine(), "q"))
            {
                if (string.Equals(command, "pause"))
                    manager.OnNext(true);
                else if (string.Equals(command, "play"))
                    manager.OnNext(false);
            }

            tokenSource.Cancel();
        }
    }

    public static class ObservableExtensions
    {
        public static IObservable<T> Pausable<T>(
            this IObservable<T> source,
            IObservable<bool> management)
        {
            return Observable.Create<T>(
                // gets called on each subscription
                observer =>
                {
                    // each time the SerialDisposable's Disposable is set
                    // the previous disposable is disposed and replaced with the latest
                    SerialDisposable subscriptions = new SerialDisposable();

                    var empty = Observable.Empty<T>();

                    // takes the source sequence and manages
                    // subscription via one (and only one) subscription
                    IDisposable subscription = source.Publish(connectable =>
                    {

                        // creates replay subjects, making sure to
                        // reset the old subscription manager
                        Func<ReplaySubject<T>> replaySubjectFactory = () =>
                        {
                            var replaySubject = new ReplaySubject<T>();

                            // if we are paused, stop listening to an empty observable
                            // if we are playing, stop listening to an active observable

                            // not sure why we have two
                            subscriptions.Disposable = connectable.Subscribe(replaySubject);

                            return replaySubject;
                        };

                        // use the factory to create the replacement manager of
                        // either the paused (empty) or playing sequence
                        var replacementReplaySubject = replaySubjectFactory();

                        // controller selector which determines which
                        // sequence we listen to either paused or playing
                        Func<bool, IObservable<T>> manager = paused =>
                        {
                            // each run of this function handles a different
                            // Boolean value from its previous run
                            if (paused)
                            {
                                replacementReplaySubject = replaySubjectFactory();

                                return empty;
                            }
                            else
                            {
                                // whenever we're no longer paused the subject's
                                // manager is disposed
                                // whenever our empty management subject terminates
                                // the values in the source sequence starts pumping
                                // values to the subscribers of the replaySubject
                                return replacementReplaySubject.Concat(connectable);
                            }
                        };

                        // return the management sequence
                        return management
                            // begin in the "playing" mode
                            .StartWith(false)
                            // only process the sequence when the value changes
                            // from playing to paused and back again
                            .DistinctUntilChanged()
                            // transform each value into ???
                            .Select(manager)
                            // subscribe to either the empty (paused) sequence or the "playing"
                            // while dropping a subscription to the previous sequence (playing if paused, paused if playing)
                            .Switch();
                    }).Subscribe(observer); // subscribe only to the derived sequence which is managed by the Boolean sequence

                    return new CompositeDisposable(subscription, subscriptions);
                });
        }
    }
}
