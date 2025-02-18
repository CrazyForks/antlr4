/* Copyright (c) 2012-2017 The ANTLR Project. All rights reserved.
 * Use of this file is governed by the BSD 3-clause license that
 * can be found in the LICENSE.txt file in the project root.
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Sharpen;

namespace Antlr4.Runtime.Atn
{
    public class ATN
    {
        public const int INVALID_ALT_NUMBER = 0;

        [NotNull]
        public readonly IList<ATNState> states = new List<ATNState>();

        /// <summary>
        /// Each subrule/rule is a decision point and we must track them so we
        /// can go back later and build DFA predictors for them.
        /// </summary>
        /// <remarks>
        /// Each subrule/rule is a decision point and we must track them so we
        /// can go back later and build DFA predictors for them.  This includes
        /// all the rules, subrules, optional blocks, ()+, ()* etc...
        /// </remarks>
        [NotNull]
        public readonly IList<DecisionState> decisionToState = new List<DecisionState>();

        /// <summary>Maps from rule index to starting state number.</summary>
        /// <remarks>Maps from rule index to starting state number.</remarks>
        public RuleStartState[] ruleToStartState;

        /// <summary>Maps from rule index to stop state number.</summary>
        /// <remarks>Maps from rule index to stop state number.</remarks>
        public RuleStopState[] ruleToStopState;

        [NotNull]
        public readonly IDictionary<string, TokensStartState> modeNameToStartState = new Dictionary<string, TokensStartState>();

        /// <summary>The type of the ATN.</summary>
        /// <remarks>The type of the ATN.</remarks>
        public readonly ATNType grammarType;

        /// <summary>The maximum value for any symbol recognized by a transition in the ATN.</summary>
        /// <remarks>The maximum value for any symbol recognized by a transition in the ATN.</remarks>
        public readonly int maxTokenType;

        /// <summary>For lexer ATNs, this maps the rule index to the resulting token type.</summary>
        /// <remarks>
        /// For lexer ATNs, this maps the rule index to the resulting token type.
        /// For parser ATNs, this maps the rule index to the generated bypass token
        /// type if the
        /// <see cref="ATNDeserializationOptions.GenerateRuleBypassTransitions()"/>
        /// deserialization option was specified; otherwise, this is
        /// <see langword="null"/>
        /// .
        /// </remarks>
        public int[] ruleToTokenType;

        /// <summary>
        /// For lexer ATNs, this is an array of
        /// <see cref="ILexerAction"/>
        /// objects which may
        /// be referenced by action transitions in the ATN.
        /// </summary>
        public ILexerAction[] lexerActions;

        [NotNull]
        public readonly IList<TokensStartState> modeToStartState = new List<TokensStartState>();

        private readonly PredictionContextCache contextCache = new PredictionContextCache();

        [NotNull]
		public DFA[] decisionToDFA = Collections.EmptyList<DFA>();

        [NotNull]
		public DFA[] modeToDFA = Collections.EmptyList<DFA>();

        protected internal readonly ConcurrentDictionary<int, int> LL1Table = new ConcurrentDictionary<int, int>();

        /// <summary>Used for runtime deserialization of ATNs from strings</summary>
        public ATN(ATNType grammarType, int maxTokenType)
        {
            this.grammarType = grammarType;
            this.maxTokenType = maxTokenType;
        }


        public virtual PredictionContext GetCachedContext(PredictionContext context)
        {
            return PredictionContext.GetCachedContext(context, contextCache, new PredictionContext.IdentityHashMap());
        }

        /// <summary>
        /// Compute the set of valid tokens that can occur starting in state
        /// <paramref name="s"/>
        /// .
        /// If
        /// <paramref name="ctx"/>
        /// is
        /// <see cref="EmptyPredictionContext.Instance"/>
        /// , the set of tokens will not include what can follow
        /// the rule surrounding
        /// <paramref name="s"/>
        /// . In other words, the set will be
        /// restricted to tokens reachable staying within
        /// <paramref name="s"/>
        /// 's rule.
        /// </summary>
        [return: NotNull]
        public virtual IntervalSet NextTokens(ATNState s, RuleContext ctx)
        {
            LL1Analyzer anal = new LL1Analyzer(this);
            IntervalSet next = anal.Look(s, ctx);
            return next;
        }

        /// <summary>
        /// Compute the set of valid tokens that can occur starting in
        /// <paramref name="s"/>
        /// and
        /// staying in same rule.
        /// <see cref="TokenConstants.EPSILON"/>
        /// is in set if we reach end of
        /// rule.
        /// </summary>
        [return: NotNull]
        public virtual IntervalSet NextTokens(ATNState s)
        {
            if (s.nextTokenWithinRule != null)
            {
                return s.nextTokenWithinRule;
            }
			s.nextTokenWithinRule = NextTokens(s, null);
            s.nextTokenWithinRule.SetReadonly(true);
            return s.nextTokenWithinRule;
        }

        public virtual void AddState(ATNState state)
        {
            if (state != null)
            {
                state.atn = this;
                state.stateNumber = states.Count;
            }
            states.Add(state);
        }

        public virtual void RemoveState(ATNState state)
        {
            states[state.stateNumber] = null;
        }

        // just free mem, don't shift states in list
        public virtual void DefineMode(string name, TokensStartState s)
        {
            modeNameToStartState[name] = s;
            modeToStartState.Add(s);
            modeToDFA = Arrays.CopyOf(modeToDFA, modeToStartState.Count);
            modeToDFA[modeToDFA.Length - 1] = new DFA(s);
            DefineDecisionState(s);
        }

        public virtual int DefineDecisionState(DecisionState s)
        {
            decisionToState.Add(s);
            s.decision = decisionToState.Count - 1;
            decisionToDFA = Arrays.CopyOf(decisionToDFA, decisionToState.Count);
            decisionToDFA[decisionToDFA.Length - 1] = new DFA(s, s.decision);
            return s.decision;
        }

        public virtual DecisionState GetDecisionState(int decision)
        {
            if (decisionToState.Count != 0)
            {
                return decisionToState[decision];
            }
            return null;
        }

        public virtual int NumberOfDecisions
        {
            get
            {
                return decisionToState.Count;
            }
        }

        /// <summary>
        /// Computes the set of input symbols which could follow ATN state number
        /// <paramref name="stateNumber"/>
        /// in the specified full
        /// <paramref name="context"/>
        /// . This method
        /// considers the complete parser context, but does not evaluate semantic
        /// predicates (i.e. all predicates encountered during the calculation are
        /// assumed true). If a path in the ATN exists from the starting state to the
        /// <see cref="RuleStopState"/>
        /// of the outermost context without matching any
        /// symbols,
        /// <see cref="TokenConstants.EOF"/>
        /// is added to the returned set.
        /// <p>If
        /// <paramref name="context"/>
        /// is
        /// <see langword="null"/>
        /// , it is treated as
        /// <see cref="ParserRuleContext.EmptyContext"/>
        /// .</p>
        /// </summary>
        /// <param name="stateNumber">the ATN state number</param>
        /// <param name="context">the full parse context</param>
        /// <returns>
        /// The set of potentially valid input symbols which could follow the
        /// specified state in the specified context.
        /// </returns>
        /// <exception cref="System.ArgumentException">
        /// if the ATN does not contain a state with
        /// number
        /// <paramref name="stateNumber"/>
        /// </exception>
        [return: NotNull]
        public virtual IntervalSet GetExpectedTokens(int stateNumber, RuleContext context)
        {
            if (stateNumber < 0 || stateNumber >= states.Count)
            {
                throw new ArgumentException("Invalid state number.");
            }
            RuleContext ctx = context;
            ATNState s = states[stateNumber];
            IntervalSet following = NextTokens(s);
            if (!following.Contains(TokenConstants.EPSILON))
            {
                return following;
            }
            IntervalSet expected = new IntervalSet();
            expected.AddAll(following);
            expected.Remove(TokenConstants.EPSILON);
            while (ctx != null && ctx.invokingState >= 0 && following.Contains(TokenConstants.EPSILON))
            {
                ATNState invokingState = states[ctx.invokingState];
                RuleTransition rt = (RuleTransition)invokingState.Transition(0);
                following = NextTokens(rt.followState);
                expected.AddAll(following);
                expected.Remove(TokenConstants.EPSILON);
                ctx = ctx.Parent;
            }
            if (following.Contains(TokenConstants.EPSILON))
            {
                expected.Add(TokenConstants.EOF);
            }
            return expected;
        }
    }
}
