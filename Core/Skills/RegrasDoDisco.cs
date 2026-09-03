namespace Jandirus.Core.Skills;

/// <summary>
/// CARREGA AS REGRAS DE NIVEL do `niveis.json` que o AssetPipeline extrai (comando `effector`).
///
/// POR QUE ISTO E SEPARADO DO <see cref="RegrasDeNivel"/>: aquele arquivo tem uma tabela escrita
/// a mao com um punhado de skills -- foi o que permitiu o motor nascer testavel antes de existir
/// extracao. Esta classe e a porta pro lote EXTRAIDO, que e 101 skills e 342 degraus. As duas
/// convivem porque `Registrar` sobrescreve por path: o que vier do disco vence a semente.
///
/// O FORMATO E UMA LISTA PLANA de tres tipos de registro (`skill`, `exp`, `degrau`), e nao um
/// objeto aninhado por skill. Nao e capricho: o leitor de JSON deste projeto e um parser de meia
/// pagina que fatia por `{`..`}` de primeiro nivel, e chave aninhada quebraria TUDO dali pra
/// frente em silencio. O extrator emite plano de proposito -- formato serve leitor.
/// </summary>
public static class RegrasDoDisco
{
	/// <summary>
	/// O QUE FICOU DE FORA na ultima carga -- ganhos por estado aceitos, por contador (que sao de
	/// evento e nao deste caminho) e condicoes que este port nao sabe avaliar.
	///
	/// Existe porque "regra descartada em silencio" e o defeito recorrente deste projeto. Se um dia
	/// alguem portar mais condicoes, este numero e o que diz quantas faltam.
	/// </summary>
	public static int GanhosPorEstado => _porEstado;
	public static int GanhosPorContador => _porContador;
	public static int CondicoesNaoEntendidas => _condDesconhecida;

	private static int _porEstado, _porContador, _condDesconhecida;

	/// <summary>
	/// O LOTE DO DISCO JA ENTROU NESTE PROCESSO? O registro (`RegrasDeNivel`) e estatico e um so: no
	/// `--host` o servidor o carrega no boot e o cliente do mesmo processo nao precisa ler o arquivo
	/// de novo; quem disca (`--connect`) e quem paga a leitura (ver `MenuJogo.CarregarNiveisNoCliente`).
	/// </summary>
	public static bool Carregado { get; private set; }

	/// <summary>Le e registra. Devolve quantas regras entraram.</summary>
	public static int Carregar(string json)
	{
		_porEstado = _porContador = _condDesconhecida = 0;
		// path -> regra em construcao. Os registros de uma mesma skill vem em sequencia, mas
		// depender disso seria depender da ordem do extrator; um mapa nao depende.
		var emObra = new Dictionary<string, RegraDeNivel>(StringComparer.OrdinalIgnoreCase);
		var degraus = new Dictionary<string, List<Degrau>>(StringComparer.OrdinalIgnoreCase);

		// (path, cadeia) das correntes em que ALGUM ramo nao foi entendido -- ver o bloco no `case "exp"`
		var quebradas = new HashSet<(string, int)>();

		foreach (string bloco in Blocos(json))
		{
			string tipo = Str(bloco, "tipo");
			string path = Str(bloco, "path");
			if (path.Length == 0) continue;

			switch (tipo)
			{
				case "skill":
				{
					RegraDeNivel r = Regra(emObra, path);
					double b = Num(bloco, "barreira", NiveisDeSkill.BarreiraPadrao);
					// barreira 0 no extrator quer dizer "a skill nao declarou" -- e ai vale o
					// padrao do DM, nao zero (que faria a skill subir de nivel todo tique)
					r.Barreira = b > 0 ? b : NiveisDeSkill.BarreiraPadrao;

					double taxa = Num(bloco, "curvataxa", 0);
					r.Crescimento = taxa > 0 ? taxa : 1;
					break;
				}

				case "exp":
				{
					RegraDeNivel r = Regra(emObra, path);
					// SO E "POR TEMPO" QUEM SOBE SOZINHO. O extrator marca `contador` quando o
					// exp vem de uma acao contada (golpes disparados, blasts); nesse caso quem
					// credita e o evento, nao o relogio -- ver `NiveisDeSkill.Creditar`.
					bool porContador = Str(bloco, "contador").Length > 0;
					if (!porContador && Num(bloco, "prob", 0) > 0)
					{
						r.GanhoPorTempo = true;
						break;
					}

					// GANHO POR CONTADOR DE EVENTO. Ate aqui este `if` fazia `_porContador++; break;`
					// -- contava a regra pro log e a DESCARTAVA, entao as 30 pericias de Ki nunca
					// tinham como creditar exp nenhum, mesmo que alguem chamasse o `Creditar`.
					// Ver `RegraDeNivel.PorContador`. O contador de diagnostico continua subindo:
					// ele agora conta quantas ENTRARAM, e nao quantas se perderam.
					if (porContador)
					{
						double porEvento = Num(bloco, "quanto", 0);
						if (porEvento <= 0) break;
						r.PorContador.Add(new RegraDeNivel.GanhoPorContador
						{
							Contador = Str(bloco, "contador"),
							Quanto = porEvento,
						});
						_porContador++;
						break;
					}

					string cond = Str(bloco, "cond").Replace(" ", "");
					int cadeia = (int)Num(bloco, "cadeia", 0);

					// O PORTAO DE NIVEL SAI DA FRENTE DA CONDICAO: `level<10&&savant.med` e
					// "meditando, ate o nivel 10" -- duas coisas, e so uma delas e estado do corpo.
					// Sem tirar o prefixo, as quatro condicoes assim caiam inteiras em
					// `_condDesconhecida` (e com elas as tres skills Basic que so tem elas).
					int nivelMenorQue = 0;
					if (cond.StartsWith("level<", StringComparison.Ordinal))
					{
						int amp = cond.IndexOf("&&", StringComparison.Ordinal);
						string num = amp > 0 ? cond[6..amp] : cond[6..];
						if (int.TryParse(num, out int teto) && teto > 0)
						{
							nivelMenorQue = teto;
							cond = amp > 0 ? cond[(amp + 2)..] : "";
						}
					}

					RegraDeNivel.Estado? quando = cond switch
					{
						"" => RegraDeNivel.Estado.Sempre,
						"savant.med" => RegraDeNivel.Estado.Meditando,
						"savant.flight" => RegraDeNivel.Estado.Voando,
						"savant.train" => RegraDeNivel.Estado.Treinando,
						"!savant.med&&!savant.flight" => RegraDeNivel.Estado.Ocioso,
						// EM LUTA (`IsInFight`): a fonte de exp da Holy Trinity e das artes marciais do corpo.
						// Ate aqui as duas caiam em `_condDesconhecida` -- a Trindade nunca subia de nivel.
						"savant.IsInFight" => RegraDeNivel.Estado.Lutando,
						"savant.train||savant.IsInFight" => RegraDeNivel.Estado.TreinandoOuLutando,

						// ---- as sete da arvore da Mente (ver `RegraDeNivel.Estado`) ----
						"else" => RegraDeNivel.Estado.Senao,
						"savant.studying" => RegraDeNivel.Estado.Estudando,
						"savant.observingnow" => RegraDeNivel.Estado.Observando,
						"savant.kibuffon" => RegraDeNivel.Estado.ComBuffDeKi,
						"savant.kiratio>1" => RegraDeNivel.Estado.KiAcimaDoNormal,
						"savant.Ki!=lastki&&diffki<0" => RegraDeNivel.Estado.GastandoKi,
						"(savant.Ki/savant.MaxKi)<0.9" => RegraDeNivel.Estado.TanqueAbaixoDe90,
						"savant.deepmeditation" => RegraDeNivel.Estado.MeditacaoProfunda,
						_ => null,
					};

					if (quando is not { } q)
					{
						_condDesconhecida++;
						// ============================ UMA CORRENTE QUEBRADA SAI INTEIRA ============================
						// Se um ramo de um `if/else` nao foi entendido, os OUTROS ramos daquela corrente
						// tambem tem que sair: sem o ramo perdido, o `else` (que vale sempre) passaria a
						// render em situacoes que no DM caem no ramo que se perdeu. Melhor nao creditar do
						// que creditar demais em silencio -- e hoje isto nao remove nada (nenhuma corrente
						// do `niveis.json` mistura condicao lida e nao lida), mas a proxima pode misturar.
						// ======================================================================================
						if (cadeia != 0) quebradas.Add((path, cadeia));
						break;
					}

					double quanto = Num(bloco, "quanto", 0);
					if (quanto <= 0) break;
					r.PorEstado.Add(new RegraDeNivel.GanhoPorEstado
					{
						Quanto = quanto,
						Quando = q,
						Cadeia = cadeia,
						NivelMenorQue = nivelMenorQue,
						NivelMinimo = (int)Num(bloco, "nivelmin", 0),
						// `curva: 1` = passou pelo `KiSkillGains` do DM. Ver `GanhoPorEstado.Curva`.
						Curva = Num(bloco, "curva", 0) > 0,
					});
					_porEstado++;
					break;
				}

				case "degrau":
				{
					int nivel = (int)Num(bloco, "nivel", -1);
					int periodo = (int)Num(bloco, "periodo", 0);
					// ============================ O PERIODICO ENTRA, E E UM DEGRAU SO ============================
					// Ate aqui `if(level % 5 == 0)` era descartado com a justificativa de que "expandir isso
					// em vinte degraus iguais seria inventar uma estrutura". Nao se expande nada: o degrau
					// guarda o PERIODO e quem aplica conta quantas vezes ele ja rendeu (`RegraDeNivel.Vezes`).
					// Eram 93 degraus fora -- o grosso do ganho das 30 maestrias de Ki. Ver `Degrau.Periodo`.
					// ==========================================================================================
					if (nivel < 0 && periodo <= 0) break;

					var d = new Degrau { Nivel = Math.Max(nivel, 0), Periodo = Math.Max(periodo, 0), Aviso = Str(bloco, "msg") };
					Pares(bloco, "buffs", d.Buffs);
					Pares(bloco, "mults", d.Mults);   // `SpiritBallCost /= 2` -- ver `Degrau.Mults`
					Pares(bloco, "genes", d.Genes);   // `add_to_stat("Energy Level", 0.05)` -- ver `Degrau.Genes`

					// AS CHAVES (`campo = n`). Canal separado dos buffs -- ver `Degrau.Flags`.
					Pares(bloco, "flags", d.Flags);
					// A BARREIRA TROCADA NO DEGRAU vem como flag do extrator (`expbarrier=20`) porque la
					// ela e uma atribuicao como as outras; aqui ela NAO e campo do lutador, e da regra --
					// sai das flags e vai pro `Degrau.Barreira`, que o `BarreiraEm` le.
					if (d.Flags.Remove("expbarrier", out double barreira) && barreira > 0) d.Barreira = barreira;

					d.Verbos = Lista(bloco, "verbos");
					// O VERB POR CASA ("Van-sama|Taunt") -- ver `Degrau.VerbosPorCasa`. Mesmo formato plano
					// `rotulo|valor` das casas do `skills.json`, pelo mesmo motivo (o leitor fatia por `{`..`}`).
					var porCasa = new List<(string, string)>();
					foreach (string item in Lista(bloco, "verbosporcasa"))
					{
						int ib = item.IndexOf('|');
						if (ib <= 0 || ib >= item.Length - 1) continue;
						porCasa.Add((item[..ib], item[(ib + 1)..]));
					}
					d.VerbosPorCasa = [.. porCasa];
					d.Destrava = Lista(bloco, "destrava");
					// `path=nivel`: a skill concedida e o `baselevel` do `learn()` (Mind.dm:104 passa 1,
					// :110 passa 0). Sem `=`, vale 0 -- o que o DM faz com quem compra.
					var concede = new List<(string, int)>();
					foreach (string item in Lista(bloco, "concede"))
					{
						int ig = item.IndexOf('=');
						if (ig < 0) { concede.Add((item, 0)); continue; }
						concede.Add((item[..ig], int.TryParse(item[(ig + 1)..], out int baselevel) ? baselevel : 0));
					}
					d.Concede = [.. concede];

					if (d.Buffs.Count == 0 && d.Mults.Count == 0 && d.Genes.Count == 0 && d.Flags.Count == 0
						&& d.Verbos.Length == 0 && d.VerbosPorCasa.Length == 0 && d.Destrava.Length == 0
						&& d.Concede.Length == 0 && d.Barreira <= 0 && d.Aviso.Length == 0) break;

					if (!degraus.TryGetValue(path, out List<Degrau>? l)) degraus[path] = l = [];
					l.Add(d);
					break;
				}
			}
		}

		// AS CORRENTES QUEBRADAS SAEM DEPOIS DA PASSADA, e nao durante: o ramo que nao foi entendido
		// pode ser o ULTIMO da corrente, e ai os anteriores ja teriam entrado.
		foreach ((string path, int cadeia) in quebradas)
			if (emObra.TryGetValue(path, out RegraDeNivel? rq))
				_porEstado -= rq.PorEstado.RemoveAll(g => g.Cadeia == cadeia);

		int n = 0;
		foreach ((string path, RegraDeNivel r) in emObra)
		{
			// exatos por nivel, e os periodicos depois (do menor periodo pro maior) -- a ordem so
			// importa pra quem imprime; quem aplica pergunta `Vezes` degrau a degrau
			if (degraus.TryGetValue(path, out List<Degrau>? l))
				r.Degraus = [.. l.OrderBy(d => d.Periodo > 0 ? 1000 + d.Periodo : d.Nivel)];
			// SKILL SEM DEGRAU NENHUM NAO VIRA REGRA. Ela subiria de nivel pra sempre sem nada
			// acontecer -- barulho no save e no relatorio, efeito zero na tela.
			if (r.Degraus.Length == 0) continue;
			RegrasDeNivel.Registrar(r);
			n++;
		}
		Carregado = true;
		return n;
	}

	private static RegraDeNivel Regra(Dictionary<string, RegraDeNivel> mapa, string path)
	{
		if (!mapa.TryGetValue(path, out RegraDeNivel? r)) mapa[path] = r = new RegraDeNivel { Path = path };
		return r;
	}

	/// <summary>Le uma lista plana de "campo=valor" pra dentro de um dicionario (os quatro canais do degrau).</summary>
	private static void Pares(string bloco, string chave, Dictionary<string, double> destino)
	{
		foreach (string par in Lista(bloco, chave))
		{
			int ig = par.IndexOf('=');
			if (ig <= 0) continue;
			if (double.TryParse(par[(ig + 1)..], System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out double v))
				destino[par[..ig]] = v;
		}
	}

	// ---- o mesmo leitor de meia pagina dos outros catalogos ----
	private static IEnumerable<string> Blocos(string s)
	{
		int i = 0;
		while (true)
		{
			int a = s.IndexOf('{', i);
			if (a < 0) yield break;
			int b = s.IndexOf('}', a);
			if (b < 0) yield break;
			yield return s[(a + 1)..b];
			i = b + 1;
		}
	}

	private static string Str(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return "";
		int a = bloco.IndexOf('"', bloco.IndexOf(':', i) + 1);
		if (a < 0) return "";
		var sb = new System.Text.StringBuilder();
		for (int k = a + 1; k < bloco.Length; k++)
		{
			if (bloco[k] == '\\' && k + 1 < bloco.Length) { sb.Append(bloco[++k]); continue; }
			if (bloco[k] == '"') break;
			sb.Append(bloco[k]);
		}
		return sb.ToString();
	}

	private static double Num(string bloco, string chave, double padrao)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return padrao;
		int a = bloco.IndexOf(':', i) + 1;
		int b = a;
		while (b < bloco.Length && (char.IsDigit(bloco[b]) || bloco[b] is '.' or '-' or ' ')) b++;
		return double.TryParse(bloco[a..b].Trim(), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : padrao;
	}

	private static string[] Lista(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return [];
		int a = bloco.IndexOf('[', i);
		int b = bloco.IndexOf(']', a + 1);
		if (a < 0 || b < 0) return [];
		var l = new List<string>();
		string dentro = bloco[(a + 1)..b];
		int k = 0;
		while (true)
		{
			int q1 = dentro.IndexOf('"', k);
			if (q1 < 0) break;
			int q2 = dentro.IndexOf('"', q1 + 1);
			if (q2 < 0) break;
			l.Add(dentro[(q1 + 1)..q2]);
			k = q2 + 1;
		}
		return [.. l];
	}
}
