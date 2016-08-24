using System;
using System.Collections.Generic;

namespace LexicalAnalysis
{

	//let's use a concept of a "character predicate"
	//in addition to a map to identify transitions,
	//that way we can use functions like IsWhitespace()
	//to represent character classes
	//Also, instances of this interface are used as
	//dictionary keys, so in some cases for efficiency
	//it might make sense to implement HashCode() and
	//Equals() so that functionally identical objects
	//test equal
	public interface ICharacterPredicate
	{
		bool IsTrueOf (char c);
	}


	public class FSM
	{

		private class FSMNode {
			public ISet<FSMNode> epsilonTransitions;
			public Dictionary<char, ISet<FSMNode>> transitionMap;
			public Dictionary<ICharacterPredicate, ISet<FSMNode>> predicateMap;

			public FSMNode() {
				this.epsilonTransitions = new HashSet<FSMNode>();
				this.transitionMap = new Dictionary<char,ISet<FSMNode>>();
				this.predicateMap = new Dictionary<ICharacterPredicate, ISet<FSMNode>>();
			}

			//abstract away the registration process because it's easier
			//to specify at call site
			public void RegisterNewStatesForChar(char c, Action<FSMNode> registration) {
				if (transitionMap.ContainsKey (c)) {
					foreach (FSMNode dest in transitionMap[c]) {
						registration.Invoke(dest);
					}
				}
				foreach(var item in predicateMap) {
					if(item.Key.IsTrueOf(c)) {
						foreach(FSMNode dest in item.Value) {
							registration.Invoke(dest);
						}
					}
				}
			}

			public void SupplementWithTransitionsFromOtherNode(FSMNode other) {
				foreach (FSMNode epsilonTransition in other.epsilonTransitions) {
					this.epsilonTransitions.Add (epsilonTransition);
				}
				foreach (var item in other.transitionMap) {
					if (!this.transitionMap.ContainsKey (item.Key)) {
						this.transitionMap [item.Key] = new HashSet<FSMNode> ();
					}
					foreach (FSMNode node in item.Value) {
						this.transitionMap [item.Key].Add (node);
					}
				}
				foreach (var item in other.predicateMap) {
					if (!this.predicateMap.ContainsKey (item.Key)) {
						this.predicateMap [item.Key] = new HashSet<FSMNode> ();
					}
					foreach (FSMNode node in item.Value) {
						this.predicateMap [item.Key].Add (node);
					}
				}
			}
		}

		//performance-wise, it's more efficient if
		//we require that after an FSM is operated
		//on (i.e., closed, or union/cat'd with
		//another FSM) it is no longer usable for
		//anything else.  This way we don't take
		//the penalty of having to deep copy the
		//entire state graph each time an operation
		//is performed.
		private bool stale;
		private readonly FSMNode initialState;
		private readonly ISet<FSMNode> finalStates;

		private FSM (FSMNode initialState, ISet<FSMNode> finalStates)
		{
			stale = false;
			this.initialState = initialState;
			this.finalStates = finalStates;
		}

		private FSM () : this(new FSMNode(), new HashSet<FSMNode>()) {}

		public static FSM ForEmptyString() {
			FSM ret = new FSM ();
			ret.finalStates.Add (ret.initialState);
			return ret;
		}

		public static FSM ForChar(char a) {
			FSM ret = new FSM ();
			FSMNode final = new FSMNode ();
			ISet<FSMNode> destSet = new HashSet<FSMNode> ();
			destSet.Add (final);
			ret.finalStates.Add (final);
			ret.initialState.transitionMap [a] = destSet;
			return ret;
		}

		public static FSM ForPredicate(ICharacterPredicate pred) {
			FSM ret = new FSM ();
			FSMNode final = new FSMNode ();
			ISet<FSMNode> destSet = new HashSet<FSMNode> ();
			destSet.Add (final);
			ret.finalStates.Add (final);
			ret.initialState.predicateMap[pred] = destSet;
			return ret;
		}

		private void CheckNotStale(string description) {
			if (this.stale) {
				throw new ArgumentException (description + " is stale");
			}
		}

		public FSM Union(FSM other) {
			this.CheckNotStale ("this");
			this.stale = true;
			other.CheckNotStale ("other");
			other.stale = true;
			//let's use no epsilon transitions and merge instead,
			//since it's pretty simple
			FSM ret = new FSM ();
			ret.initialState.SupplementWithTransitionsFromOtherNode (this.initialState);
			ret.initialState.SupplementWithTransitionsFromOtherNode (other.initialState);
			foreach (FSMNode node in this.finalStates) {
				ret.finalStates.Add (node);
			}
			foreach (FSMNode node in other.finalStates) {
				ret.finalStates.Add (node);
			}
			return ret;
		}

		public FSM Concatenation(FSM other) {
			this.CheckNotStale ("this");
			this.stale = true;
			other.CheckNotStale ("other");
			other.stale = true;
			//let's use no epsilon transitions and merge instead,
			//since it's pretty simple
			FSM ret = new FSM (this.initialState, other.finalStates);
			foreach (FSMNode node in this.finalStates) {
				node.SupplementWithTransitionsFromOtherNode (other.initialState);
			}
			return ret;
		}

		public FSM Closure() {
			this.CheckNotStale ("this");
			this.stale = true;
			//it's possible to do this without epsilon transitions,
			//but it's simpler this way
			FSM ret = new FSM (this.initialState, this.finalStates);
			foreach (FSMNode node in ret.finalStates) {
				node.epsilonTransitions.Add (ret.initialState);
			}
			return ret;
		}

		public static FSM ForConstantStringPattern(string st) {
			FSM fsm = FSM.ForEmptyString();
			foreach(char c in st) {
				fsm = fsm.Concatenation(FSM.ForChar(c));
			}
			return fsm;
		}

		private class RunnableFSM : IRecognizerFSA
		{
			private readonly FSMNode initialState;
			private readonly ISet<FSMNode> finalStates;
			private ISet<FSMNode> currStates;

			//if false, then after we reach the first accepting
			//state we refuse to recognize anything else
			private readonly bool max;

			public RunnableFSM(FSMNode initialState, ISet<FSMNode> finalStates, bool max) {
				this.initialState = initialState;
				this.finalStates = finalStates;
				this.max = max;
			}

			public void Reset() {
				currStates = new HashSet<FSMNode> ();
				currStates.Add (initialState);
			}

			public void ProcessChar(char c) {
				//in min mode, if we've already accepted,
				//then we can never accept again
				if (!max && IsAccepting()) {
					currStates.Clear ();
				}

				ISet<FSMNode> newStates = new HashSet<FSMNode> ();
				//we need to DFS through epsilon transitions, let's
				//use a stack
				Stack<FSMNode> epsilonClosureStack = new Stack<FSMNode> ();
				foreach(FSMNode state in currStates) {
					state.RegisterNewStatesForChar (c, delegate(FSMNode obj) {
						if (!newStates.Contains (obj)) {
							epsilonClosureStack.Push (obj);
							newStates.Add (obj);
						}
					});
				}
				while(epsilonClosureStack.Count != 0) {
					FSMNode top = epsilonClosureStack.Pop();
					foreach(FSMNode epsilonDest in top.epsilonTransitions) {
						if(!newStates.Contains(epsilonDest)) {
							epsilonClosureStack.Push(epsilonDest);
							newStates.Add(epsilonDest);
						}
					}
				}
				this.currStates = newStates;
			}

			public bool IsAccepting() {
				foreach (FSMNode curr in this.currStates) {
					if (finalStates.Contains (curr)) {
						return true;
					}
				}
				return false;
			}

			public bool CanBeAccepting() {
				//we could actually iterate through all current
				//states and see if they have transitions, but
				//it's simpler to return false iff curr states
				//is empty, it won't break anything
				return currStates.Count != 0;
			}

		}

		public IRecognizerFSA AsRecognizerFSA() {
			this.CheckNotStale ("this");
			this.stale = true;
			return new RunnableFSM (this.initialState, this.finalStates, true);
		}

		public IRecognizerFSA AsMinRecognizerFSA() {
			this.CheckNotStale ("this");
			this.stale = true;
			return new RunnableFSM (this.initialState, this.finalStates, false);
		}

	}
}
