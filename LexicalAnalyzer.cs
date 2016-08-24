using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LexicalAnalysis
{

	/// <summary>
	/// Interface for abstractly representing
	/// a finite state automaton which is used
	/// to recognize a pattern of text.
	/// </summary>
	public interface IRecognizerFSA
	{
		/// <summary>
		/// Reset this instance to its initial state.
		/// </summary>
		void Reset();
		/// <summary>
		/// Process a char, changing internal state as appropriate.
		/// 
		/// This method should never be called unless CanBeAccepting()
		/// returns true.
		/// </summary>
		/// <param name="c">Char to process</param>
		void ProcessChar(char c);
		/// <summary>
		/// Determines whether this instance is in an accepting state or,
		/// equivalenty, recognizes the input that it has seen so far.
		/// </summary>
		/// <returns><c>true</c> if this instance is accepting; otherwise, <c>false</c>.</returns>
		bool IsAccepting();
		/// <summary>
		/// Determines whether this instance can be accepting after
		/// processing one or more additional characters.
		/// 
		/// If this IRecognizerFSA is in a state where CanBeAccepting()
		/// returns false, then the behavior of ProcessChar() is undefined,
		/// and can throw a runtime exception.
		/// 
		/// Note that it is entirely possible to be in a state where IsAccepting()
		/// returns true, but CanBeAccepting() returns false, this means that
		/// while it accepts the input it's seen so far, this input is not a
		/// strict prefix of any (longer) string that this FSA can accept.
		/// 
		/// Note that an implementation can return false positives as long
		/// as it's only for a finite number of characters.
		/// </summary>
		/// <returns><c>true</c> if this instance can be accepting; otherwise, <c>false</c>.</returns>
		bool CanBeAccepting();
	}

	/// <summary>
	/// Interface for representing an object
	/// that has a char property which can
	/// be observed.
	/// </summary>
	public interface IHasCharProperty {
		/// <summary>
		/// Get the char property associated with this instance.
		/// </summary>
		/// <returns>The char property associated with this instance.</returns>
		char GetCharProperty();
	}

	/// <summary>
	/// To specify a token type, we must provide
	/// an FSA which can recognize the token, and
	/// some action to take when the token's
	/// pattern is encountered.
	/// </summary>
	public struct TokenType<T> where T : IHasCharProperty {
		public readonly IRecognizerFSA Pattern;
		public readonly Action<IList<T>> Handler;

		public TokenType(IRecognizerFSA pattern, Action<IList<T>> handler) {
			this.Pattern = pattern;
			this.Handler = handler;
		}
	}

	/// <summary>
	/// A very simple maximum munch-style lexical analyzer.
	/// 
	/// The design assumes that rather than just parsing
	/// strings, we are parsing a sequence of objects,
	/// each of which has a char property.  If a token
	/// matches, then we would like the sequence of objects,
	/// not just the string.
	/// </summary>
	public class LexicalAnalyzer<T> where T : IHasCharProperty
	{

		//list of token types we know about, the order matters,
		//if a string matches two types, it is assigned the type
		//that is first in the list
		private readonly IList<TokenType<T>> tokenTypes;

		//action to take if we ever have an item that doesn't
		//match any token type
		private readonly Action<T> defaultAction;

		//items that have been recognized by some FSA
		private readonly IList<T> recognized;

		//items that we have seen and fed to an FSA, but have
		//not yet been recognized
		private readonly IList<T> lookAhead;

		//if we are processing characters and have found an
		//accepting type, but are looking ahead to see if
		//we can match a longer string (to implement maximal
		//munch), then this is the handler of the last type
		//that accepted the input, null if nothing has accepted
		//the input
		private Action<IList<T>> acceptingHandler;

		public LexicalAnalyzer (IList<TokenType<T>> tokenTypes, Action<T> defaultAction)
		{
			this.tokenTypes = new List<TokenType<T>>(tokenTypes);
			this.defaultAction = defaultAction;
			this.recognized = new List<T> ();
			this.lookAhead = new List<T> ();
			//make sure all FSA's have been reset
			this.Reset();
		}

		public void Reset() {
			recognized.Clear ();
			lookAhead.Clear ();
			foreach (TokenType<T> tt in tokenTypes) {
				tt.Pattern.Reset ();
			}
			acceptingHandler = null;
		}

		public void ProcessItem(T item) {

			//append to lookAhead, since it hasn't
			//been accepted yet
			this.lookAhead.Add(item);

			//when we iterate through we want to track whether
			//we've found an accepting recognizer, the reason
			//is that if multiple accept the same string we
			//break ties by using the first
			bool foundAccepting = false;

			//also track whether any can be accepting, if
			//none then we need to take special action
			bool foundCanBeAccepting = false;
			foreach (TokenType<T> tt in tokenTypes) {
				IRecognizerFSA r = tt.Pattern;
				if (r.CanBeAccepting ()) {
					foundCanBeAccepting = true;
					r.ProcessChar (item.GetCharProperty ());
					//we only care about whether or not it's accepting
					//if it's the first
					if (!foundAccepting && r.IsAccepting()) {
						foundAccepting = true;
						acceptingHandler = tt.Handler;
						//we need to append lookahead to accepted
						//and clear lookahead
						foreach(T la in lookAhead) {
							recognized.Add(la);
						}
						lookAhead.Clear ();
					}
				}
			}

			if (!foundCanBeAccepting) {
				//we're done with input until we
				//dispatch to some handler and
				//reset state
				DoneWithLookahead ();
			}

		}

		public void ProcessEOF() {
			while (this.recognized.Count != 0 || this.lookAhead.Count != 0) {
				//this next call will always decrease the value of
				//recognized.Count + lookAhead.Count by at least
				//and, and is therefore this loop will always terminate
				DoneWithLookahead ();
			}
		}

		//this is what we do when we are no longer
		//looking ahead for more tokens to find a
		//larger match
		private void DoneWithLookahead() {
			if (acceptingHandler == null) {
				Debug.Assert (recognized.Count == 0);
				Debug.Assert (lookAhead.Count != 0);
				//we throw away the first char as
				//unprocessable and try again
				this.defaultAction (lookAhead [0]);
				lookAhead.RemoveAt (0);
			} else {
				Debug.Assert (recognized.Count != 0);
				acceptingHandler (new List<T>(recognized));
			}
			//now reset and process lookahead
			IList<T> prevLookahead = new List<T> (lookAhead);
			Reset ();
			foreach (T i in prevLookahead) {
				//this is recursive when called from
				//ProcessItem(), but at each level
				//the sum recognized.Count + lookAhead.Count
				//should go down, so it should only go
				//to a finite depth
				ProcessItem(i);
			}
		}
	
	}
}
