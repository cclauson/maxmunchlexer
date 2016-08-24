using System;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;

namespace LexicalAnalysis
{

	delegate bool DgCharacterPredicate(char c);

	//let's just make an implemetation that wraps a delegate,
	//would seem to make things easy
	class DelegateBasedCharPredicate : ICharacterPredicate {
		private readonly DgCharacterPredicate pred; 
		public DelegateBasedCharPredicate(DgCharacterPredicate pred) {
			this.pred = pred;
		}
		public bool IsTrueOf(char c) {
			return pred.Invoke (c);
		}
	}

	//suppose our text is actually represented by
	//a sequence of things like this...
	struct CharIntPair
	{
		public readonly char c;
		public readonly int i;
		public CharIntPair(char c, int i) {
			this.c = c;
			this.i = i;
		}
	}

	//in order to interface CharIntPair with the lexical
	//analyzer, we need an adapter class like this
	class CharIntPairLexerAdapter : IHasCharProperty
	{
		public readonly CharIntPair cip;
		public CharIntPairLexerAdapter(CharIntPair cip) {
			this.cip = cip;
		}
		public char GetCharProperty() {
			return cip.c;
		}
	}

	class MainClass
	{

		private static void AppendLine(string line, StringBuilder sb) {
			sb.Append ("    " + line + "\n");
		}

		private static ICharacterPredicate Whitespace = new DelegateBasedCharPredicate(delegate(char c) {
			return Char.IsWhiteSpace(c);
		});

		private static ICharacterPredicate AnyChar = new DelegateBasedCharPredicate(delegate(char c) {
			return true;
		});

		private static IRecognizerFSA WhitespaceRecognizer() {
			return FSM.ForPredicate(Whitespace).Closure().AsRecognizerFSA();
		}

		private static ICharacterPredicate NotChar(char c) {
			return new DelegateBasedCharPredicate(delegate(char c2) {
				return c != c2;
			});
		}

		private static IRecognizerFSA QuotePatternRecognizer(string begin, string end) {
			return FSM.ForConstantStringPattern (begin).Concatenation (FSM.ForPredicate(AnyChar).Closure())
				.Concatenation(FSM.ForConstantStringPattern(end)).AsMinRecognizerFSA();
		}

		public static void Main (string[] args)
		{
			//some text to lex
			StringBuilder sb = new StringBuilder ();
			AppendLine ("for  in   while   //this is a comment", sb);
			AppendLine ("   ", sb);
			AppendLine ("/*", sb);
			AppendLine ("multi", sb);
			AppendLine ("line", sb);
			AppendLine ("comment", sb);
			AppendLine ("*/", sb);
			AppendLine ("fofds \"afdsafd\" whdsafdsa", sb);

			//let's turn the text into a list of char/int pairs
			IList<CharIntPair> cips = new List<CharIntPair> ();
			int i = 0;
			foreach (char c in sb.ToString()) {
				cips.Add(new CharIntPair(c, i));
				++i;
			}

			//to lex, we need a list of token types
			IList<TokenType<CharIntPairLexerAdapter>> types =
				new List<TokenType<CharIntPairLexerAdapter>>();
			//add a token type for a stretch of whitespace
			types.Add(new TokenType<CharIntPairLexerAdapter>(
				WhitespaceRecognizer(), delegate(IList<CharIntPairLexerAdapter> ciplas) {
					//add up numbers associated with objects
					int sum = 0;
					foreach(CharIntPairLexerAdapter cipla in ciplas) {
						sum += cipla.cip.i;
					}
					Console.WriteLine("Got " + ciplas.Count + " chars of whitespace with sum " + sum);
				}
			));

			//add some keywords
			types.Add(new TokenType<CharIntPairLexerAdapter>(
				FSM.ForConstantStringPattern("for").AsRecognizerFSA(), delegate(IList<CharIntPairLexerAdapter> ciplas) {
					Console.WriteLine("Got for at index " + ciplas[0].cip.i);
				}
			));
			types.Add(new TokenType<CharIntPairLexerAdapter>(
				FSM.ForConstantStringPattern("in").AsRecognizerFSA(), delegate(IList<CharIntPairLexerAdapter> ciplas) {
					Console.WriteLine("Got in at index " + ciplas[0].cip.i);
				}
			));
			types.Add (new TokenType<CharIntPairLexerAdapter> (
				FSM.ForConstantStringPattern("while").AsRecognizerFSA(), delegate(IList<CharIntPairLexerAdapter> ciplas) {
				Console.WriteLine ("Got while at index " + ciplas [0].cip.i);
			}
			));

			//add a token type for a single line comment
			types.Add(new TokenType<CharIntPairLexerAdapter>(
				QuotePatternRecognizer("//", System.Environment.NewLine), delegate(IList<CharIntPairLexerAdapter> ciplas) {
					StringBuilder sb2 = new StringBuilder();
					foreach (CharIntPairLexerAdapter cipla in ciplas) {
						sb2.Append(cipla.cip.c);
					}
					Console.WriteLine("Got single line comment:");
					Console.Write(sb2.ToString());
				}
			));

			//add a token type for a multiline comment
			types.Add(new TokenType<CharIntPairLexerAdapter>(
				QuotePatternRecognizer("/*", "*/"), delegate(IList<CharIntPairLexerAdapter> ciplas) {
					StringBuilder sb2 = new StringBuilder();
					foreach (CharIntPairLexerAdapter cipla in ciplas) {
						sb2.Append(cipla.cip.c);
					}
					Console.WriteLine("Got multiline comment:");
					Console.WriteLine(sb2.ToString());
				}
			));

			//add a token type for a string literal
			types.Add(new TokenType<CharIntPairLexerAdapter>(
				QuotePatternRecognizer("\"", "\""), delegate(IList<CharIntPairLexerAdapter> ciplas) {
					StringBuilder sb2 = new StringBuilder();
					foreach (CharIntPairLexerAdapter cipla in ciplas) {
						sb2.Append(cipla.cip.c);
					}
					Console.WriteLine("Got string literal:");
					Console.WriteLine(sb2.ToString());
				}
			));

			LexicalAnalyzer<CharIntPairLexerAdapter> lexer = new LexicalAnalyzer<CharIntPairLexerAdapter>(
				types,
				delegate(CharIntPairLexerAdapter cipla) {
					Console.WriteLine("Got unhandlable char " + cipla.cip.c + " with number " + cipla.cip.i);
				}
			);

			foreach (CharIntPair cip in cips) {
				lexer.ProcessItem(new CharIntPairLexerAdapter(cip));
			}
			lexer.ProcessEOF();

		}
	}
}
