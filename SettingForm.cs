//-----------------------------------------------------------
//
// Copyright  2023 주재넷(주)  all rights reserved.
// Programmer : 박 종 호(Jong Ho Park),
//              정 재 우(Jae Woo Jung),
//              김 도 훈(Kim Do Hoon)
// Description : 수위 계측 프로그램
// 
//-----------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Npgsql;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Newtonsoft.Json;

namespace MonWatersAdmin
{
	public partial class SettingForm : Form
	{
		//DB 접속 정보
		static string _server;
		static string _port;
		static string _db = "monwaters";
		static string _id;
		static string _pw;
		public string _psqlConnection = "";
		public string filePath = $@"C:\Users\{Environment.UserName}\.monwaters\db_info.dat";
		public static string publicKeyPath = $@"C:\Users\{Environment.UserName}\.monwaters\publicKey.xml";
		public static string privateKeyPath = $@"C:\Users\{Environment.UserName}\.monwaters\privateKey.xml";
		public static string saveOriginalVideoPath = $@"C:\Program Files (x86)\MonWatersAdmin\record_cctv.bat";

		public static string privateKey;
		public static string publicKey;

		public static string decryptedText;
		bool insertFlag = false;
		public static string tableName;
		public static int selIndex;
		public static int tableIdx;
		public static string rowIndex;

		int selectDevice;
		double pageSize = 200;
		double offset = 0;
		double nowCnt = 0;
		int count = 0;
		bool pageFlag = true;

		string AppNameStr = Application.ProductName;
		string AppVer = Application.ProductVersion;
		string copystr;

		public bool activateFormExist = false;

		private int previousSelectedIndex = -1;

		ProgressForm pg_form;
		BackgroundWorker bg_worker = new BackgroundWorker();

		NpgsqlConnection conn;
		NpgsqlDataAdapter adapter;
		public SFTPHost sftphost;
		//private System.Threading.Timer crossSectionTimer;

		public SettingForm()
		{
			InitializeComponent();
			next_btn.Image.RotateFlip(RotateFlipType.Rotate180FlipY);
			checkKeyDir();
			this.Load += SettingForm_Load;
			//crossSectionTimer = new System.Threading.Timer(crossSectionChk, null, 2000, 5000);
		}

		private void crossSectionChk(object state)
		{
			if (crossSectionPic.Visible)
			{
				var host = GetCrossSectionInfos();
				CrossSectionStream(host);
			}
		}

		//Setting Form Load시 Event
		private void SettingForm_Load(object sender, EventArgs e)
		{
			SetUp_SettingForm();
		}

		private void SettingForm_Closing(object sender, FormClosingEventArgs e)
		{
			try
			{

				if (pg_form != null)
				{
					pg_form.Close();
				}
			}
			catch (Exception)
			{

			}
		}

		//SettingForm 기본 세팅
		public void SetUp_SettingForm()
		{
			bool chk = CheckDatabase();
			Debug.WriteLine(_psqlConnection);
			if (chk)
			{
				Set_ListBox_tables();
				GetDeviceList();
				if (!Check_License_Key())
				{
				}
			}
			else
			{
				MessageBox.Show("입력된 접속 정보가 없습니다.\n[설정] - [접속 정보]를 눌러 접속 정보를 입력해주세요.", null, MessageBoxButtons.OK);
			}
		}

		//접속 정보 확인
		public bool CheckDatabase()
		{
			bool checkDB;
			if (File.Exists(filePath))
			{
				string encrypted = File.ReadAllText(filePath);
				if (File.Exists(privateKeyPath))
				{
					string privateKey = File.ReadAllText(privateKeyPath);
					DetectWaterLevel.RsaEncDec red = new DetectWaterLevel.RsaEncDec();
					decryptedText = red.Decrypt(encrypted, privateKey);
					string[] infos = decryptedText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
					_server = infos[0];
					_port = infos[1];
					_id = infos[2];
					_pw = infos[3];
					_psqlConnection = string.Format("host={0}; port={1};database={2};username={3};password={4}; ", _server, _port, _db, _id, _pw);
					//conn = new NpgsqlConnection(_psqlConnection);
				}
				checkDB = true;
			}
			else
			{
				checkDB = false;
			}
			return checkDB;
		}

		private void QueryTable()
		{
			QueryTable(null, null);
		}

		// 선택한 Table Query
		private void QueryTable(BackgroundWorker bg_worker, string addrow)
		{
			try
			{
				DataTable dataTable = new DataTable();
				try
				{
					dataTable = GetData(offset);
					if (bg_worker != null && bg_worker.CancellationPending)
					{
						return;
					}
					Init_DGV(dataTable);
					if (tableIdx == 0 || tableIdx == 1 || tableIdx == 2)
					{
						pg_form.FormClosing += (obj, args) =>
						{
							if (dataTable.Rows.Count <= 0)
							{
								MessageBox.Show("데이터가 존재하지 않습니다.\n장치 관리에서 장치를 등록해주세요.", "데이터 없음", MessageBoxButtons.OK, MessageBoxIcon.Error);
							}
						};
					}
					else if (tableIdx == 4)
					{

						pg_form.FormClosing += (obj, args) =>
						{
							if (dataTable.Rows.Count <= 0)
							{
								MessageBox.Show("데이터가 존재하지 않습니다.\n장치를 등록 후 단면 좌표를 입력해주세요.", "데이터 없음", MessageBoxButtons.OK, MessageBoxIcon.Error);
							}
						};
					}
				}
				catch (Exception)
				{
					return;
				}
				if (bg_worker != null && bg_worker.CancellationPending)
				{
					return;
				}
				if (tableQueryView.InvokeRequired)
				{
					tableQueryView.BeginInvoke((Action)delegate
					{
						if (bg_worker != null && bg_worker.CancellationPending)
						{
							return;
						}
						foreach (DataGridViewColumn column in tableQueryView.Columns)
						{
							string englishFieldName = column.DataPropertyName;
							var table = tables[tableIdx];
							if (table.TryGetValue(englishFieldName, out string koreanFieldName))
							{
								column.HeaderText = koreanFieldName;
							}
							column.Tag = englishFieldName;
							if (column.HeaderText.Contains("PW") || column.HeaderText.Contains("기상청") || column.HeaderText.Contains("라이선스"))
							{
								column.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
								column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;

								column.Width = 100;
							}
						}
						if (addrow.ToLower().Equals("true"))
						{
							tableQueryView.AllowUserToAddRows = true;
							//Init_DGV(dataTable);
						}
						else
						{
							tableQueryView.AllowUserToAddRows = false;
						}
					});
				}
				if (bg_worker != null && bg_worker.CancellationPending)
				{
					return;
				}
			}
			catch (Exception)
			{
				MessageBox.Show($"데이터 베이스를 불러오지 못했습니다.\n접속정보를 확인해주세요.", "데이터 베이스 접속 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);

			}
		}

		// 저장 버튼 클릭 시 
		private void updateBtn_MouseClick(object sender, MouseEventArgs e)
		{
			//Console.WriteLine($"insertFlag: {insertFlag}");
			if (tableName != null)
			{
				DialogResult res = MessageBox.Show("확인 버튼을 클릭시 데이터 베이스에 저장됩니다.\n저장하시겠습니까?", "데이터 입력", MessageBoxButtons.OKCancel);
				if (res == DialogResult.OK)
					if (Check_InputValue())
					{
						UpdateSettings();
					}
			}

		}
		// DB Update/Insert
		private void UpdateSettings()
		{
			try
			{
				// 현재 선택된 셀의 행 인덱스를 저장합니다.
				previousSelectedIndex = selIndex;

				using (NpgsqlConnection conn = new NpgsqlConnection(_psqlConnection))
				{
					conn.Open();

					// 현재 테이블 정보 가져오기
					XmlData selectedItem = (XmlData)listBox_tables.SelectedItem;

					// 입력값 가져오기
					var input_list = Get_InputVal();

					// `{6, PresetInfos}`는 특정 처리
					if (tableIdx == 6)
					{
						// ID 가져오기
						int id = (int)tableQueryView.Rows[Convert.ToInt32(previousSelectedIndex)].Cells["id"].Value;

						// 매개변수 추출
						int deviceId = int.Parse(input_list["deviceid"]);
						int presetNum = int.Parse(input_list["preset_num"]);
						double presetTopLevel = double.Parse(input_list["preset_top_level"]);
						double presetBotLevel = double.Parse(input_list["preset_bot_level"]);
						double topPxWidthOffset = double.Parse(input_list["top_px_width_offset"]);
						double pxWidthOffset = double.Parse(input_list["px_width_offset"]);
						double topPxPtzMokjaWidth = double.Parse(input_list["top_px_ptz_mokjawidth"]);
						double pxPtzMokjaWidth = double.Parse(input_list["px_ptz_mokjawidth"]);
						double measureHeight = double.Parse(input_list["measure_height"]);
						decimal virtualTopLevel = decimal.Parse(input_list["virtual_top_level"]);
						decimal virtualBotLevel = decimal.Parse(input_list["virtual_bot_level"]);
						string riverStartLoc = input_list["riverstartloc"];
						string riverEndLoc = input_list["riverendloc"];
						string mokjaPoints = input_list["mokjapoints"];
						string virtualMokjaImgPath = input_list["virtual_mokjaimg_path"];

						// 쿼리 호출
						string functionQuery = $@"SELECT update_preset_infos_prevent_dup_fn({id}, {deviceId}, {presetNum}, {presetTopLevel}, {presetBotLevel}, {topPxWidthOffset}, {pxWidthOffset}, {topPxPtzMokjaWidth}, {pxPtzMokjaWidth}, {measureHeight}, {virtualTopLevel}, {virtualBotLevel}, '{riverStartLoc}', '{riverEndLoc}', '{mokjaPoints}', '{virtualMokjaImgPath}');";

						NpgsqlCommand comm = new NpgsqlCommand(functionQuery, conn);
						string result = comm.ExecuteScalar().ToString();

						dynamic resultJson = JsonConvert.DeserializeObject(result);
						string res = resultJson.res.ToString();

						// 결과값에 따라 메시지 처리
						switch (res)
						{
							case "1":
								MessageBox.Show("중복된 preset_num이 존재합니다.", "중복 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
								return;

							case "-1":
								MessageBox.Show("해당 ID가 존재하지 않아 업데이트할 수 없습니다.", "업데이트 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
								return;

							case "0":
								MessageBox.Show("저장이 완료되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
								break;

							default:
								MessageBox.Show("알 수 없는 결과가 반환되었습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
								return;
						}
					}
					else
					{
						// 일반적인 테이블 처리 로직
						if (insertFlag == false)
						{
							string updateQuery = $"UPDATE {tableName} SET ";
							foreach (var input in input_list)
							{
								if (input.Value == "")
									updateQuery += $"{input.Key}=DEFAULT, ";
								else
									updateQuery += $"{input.Key}='{input.Value}', ";
							}
							updateQuery = updateQuery.Remove(updateQuery.Length - 2);
							updateQuery += $" WHERE id={rowIndex};";

							NpgsqlCommand comm = new NpgsqlCommand(updateQuery, conn);
							comm.ExecuteNonQuery();
						}
						else
						{
							string insertQuery = $"INSERT INTO {tableName} (";
							foreach (var input in input_list)
								insertQuery += $"{input.Key}, ";
							insertQuery = insertQuery.Remove(insertQuery.Length - 2);
							insertQuery += ") VALUES (";
							foreach (var input in input_list)
								insertQuery += (input.Value == "" ? "DEFAULT, " : $"'{input.Value}', ");
							insertQuery = insertQuery.Remove(insertQuery.Length - 2);
							insertQuery += ");";

							NpgsqlCommand comm = new NpgsqlCommand(insertQuery, conn);
							comm.ExecuteNonQuery();
						}

						MessageBox.Show("저장이 완료되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
					}

					// UI 및 데이터 갱신
					QueryTable();
					GetDeviceList();

					if (selectedItem.AddRow.ToLower().Equals("true"))
						tableQueryView.AllowUserToAddRows = true;
					else
						tableQueryView.AllowUserToAddRows = false;
				}
			}
			catch (Exception e)
			{
				Debug.Print("e: " + e.StackTrace);
				if (publicKey == null)
				{
					MessageBox.Show("비교 암호값이 없습니다. 고객센터에 문의하세요.", "비교 암호값 없음", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
				else
				{
					MessageBox.Show("저장에 실패했습니다. 입력정보와 접속정보를 확인해주세요.", "DB 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		//테이블별 필드Mapping
		Dictionary<int, Dictionary<string, string>> tables = new Dictionary<int, Dictionary<string, string>>{
			{0, deviceFeilds},
			{1, settingFeilds},
			{2, measureFeilds},
			{3, measureFactors},
			{4, all_infos },
			{6, PresetInfos },
		};

		// 쿼리할 필드 정리
		Dictionary<int, string> queryFields = new Dictionary<int, string>
		{
			{0, Set_queryFields(deviceFeilds)},
			{1, Set_queryFields(settingFeilds)},
			{2, Set_queryFields(measureFeilds) },
			{3, Set_queryFields(measureFactors) },
			{4, " * " },
			{6, Set_queryFields(PresetInfos) },
		};

		// 테이블별 필드
		//Device
		static Dictionary<string, string> deviceFeilds = new Dictionary<string, string>
		{
			{"id","No"},
			{"krf_id", "소하천코드번호" },
			{"observatorycode", "관측소 코드" },
			{"name", "이름" },
			{"placename", "하천명"},
			{"location","주소"},
			{"mokjatype","목자판 타입"},
			{"locx","위도"},
			{"locy","경도"},
			{"admar_sicode", "시 코드번호"},
			{"areacode", "지역 코드" },
			{"axisrtsp","CCTV 주소"},
			{"httppath", "HTTP 주소"},
			{"sftppath", "단면 이미지 저장소" },
			{"advisorylevel","주의보 수위"},
			{"warninglevel","경보 수위"},
			{"riverwidth", "하천 폭"},
			{"riverheight","영점표고(EL.m)"},
			{"riverstartloc", "측선 시작점"},
			{"riverendloc", "측선 끝점"},
			{"dt","DT"},
			{"mokjapoints", "목자 위치"},
			{"mokjapoints_level_scale", "목자 수위 크기"},
			{"measurecycle","계측 주기" },
			{"measuredigits","표기 자릿수"},
			{"virtual_top_level", "화면상 최고 수위" },
			{"virtual_bot_level", "화면상 최저 수위" },
			{"k_factor", "K-Factor" },
			{"thresh", "한계값" },
			{"measure_offset","측정 오프셋" },
			{"top_px_width_offset","상단 픽셀 너비 오프셋" },
			{"px_width_offset","하단 픽셀 너비 오프셋" },
			{"top_px_ptz_mokjawidth", "상단 ptz 목자 너비 오프셋"},
			{"px_ptz_mokjawidth", "하단 ptz 목자 너비 오프셋"},
			{"measure_height","목자판 픽셀 높이" },
			{"display_type", "표기 방식"},
			{"video_save_path", "영상 저장소" },
			{"issiding", "유속측정방식"},
			{"piv_thresh_hold", "PIV 한계값" },
			{"flowrate_period", "PIV 측정 주기" },
			{"detect_fps", "계측 FPS" },
			{"stream_fps", "스트리밍 FPS" },
			{"jpeg_quality", "프레임 퀄리티" },
			{"use_virtual", "가상 모드" },
			{"auto_track", "수면 추적기능" },
			{"siding_direction", "측선 방향" },
			{"river_flow_direction", "하천 방향" },
			{"use_flow_direction", "하천 방향 사용" },
			{"use_surface_trace", "표면 추적 사용"},
			{"of_auto", "OF 자동" },
			{"gap_zero_deepest", "수심 영점" },
			{"waterai_save_quality", "저장 퀄리티" },
			{"max_water_level", "최대 수위" },
			{"max_water_velocity", "최대 유속" },
			{"max_base_flow", "최대 기본 수위" },
			{"ptz_period", "PTZ 변경 주기" },
			{"waterai_mode", "waterai 모드" },
			{"license_id", "라이선스"},
		};

		//Settings
		static Dictionary<string, string> settingFeilds = new Dictionary<string, string> {
			{"id", "No"},
			{"jjnethost","Detect Host"},
			{"port","Detect 포트"},
			{"username","Detect ID"},
			{"password","Detect PW"},
			{"detectaiserver_path", "Detect 저장소"},
			{"monwatershost","MonWaters Host"},
			{"monwatersport", "MonWaters 포트"},
			{"monuname", "MonWaters ID"},
			{"monpassword", "MonWaters PW"},
			{"tensorflowpath","AIPIV 저장소"},
			{"monwsport", "MonWatersWS 포트"},
			{"sftpport","SFTP 포트"},
			{"dbport", "DB 포트"},
			{"myuname", "DB ID"},
			{"mypassword", "DB PW"},
			{"isblur", "블러 처리" },
			//{"ftpsavepath","FTP 저장소"},
			{"obj_path","좌표파일 저장소"},
			{"fctapikey", "기상청 API Key" },
			{"fctapiurl", "기상청 API URL" },
			{"ndm_apikey", "국립재난안전연구원 API Key" },
			{"ndm_apiurl", "국립재난안전연구원 API URL" },
		};

		//targetDatabase
		static Dictionary<string, string> connectFeilds = new Dictionary<string, string> {
			{"id","No"},
			{"ip","IP 주소"},
			{"port","포트"},
			{"userid","ID"},
			{"userpw","PW"},
			{"tablename","테이블 명"},
			//{"fieldname","필드 명"},
		};

		//measurePoint
		static Dictionary<string, string> measureFeilds = new Dictionary<string, string> {
			{"id","No"},
			{"pointx_01", "1번 측점 X좌표" },
			{"pointy_01", "1번 측점 Y좌표" },
			{"pointx_02", "2번 측점 X좌표" },
			{"pointy_02", "2번 측점 Y좌표" },
			{"pointx_03", "3번 측점 X좌표" },
			{"pointy_03", "3번 측점 Y좌표" },
			{"pointx_04", "4번 측점 X좌표" },
			{"pointy_04", "4번 측점 Y좌표" },
			{"pointx_05", "5번 측점 X좌표" },
			{"pointy_05", "5번 측점 Y좌표" },
			{"pointx_06", "6번 측점 X좌표" },
			{"pointy_06", "6번 측점 Y좌표" },
			{"pointx_07", "7번 측점 X좌표" },
			{"pointy_07", "7번 측점 Y좌표" },
			{"pointx_08", "8번 측점 X좌표" },
			{"pointy_08", "8번 측점 Y좌표" },
			{"pointx_09", "9번 측점 X좌표" },
			{"pointy_09", "9번 측점 Y좌표" },
			{"pointx_10", "10번 측점 X좌표" },
			{"pointy_10", "10번 측점 Y좌표" },
			{"pointx_11", "11번 측점 X좌표" },
			{"pointy_11", "11번 측점 Y좌표" },
			{"pointx_12", "12번 측점 X좌표" },
			{"pointy_12", "12번 측점 Y좌표" },
			{"pointx_13", "13번 측점 X좌표" },
			{"pointy_13", "13번 측점 Y좌표" },
			{"pointx_14", "14번 측점 X좌표" },
			{"pointy_14", "14번 측점 Y좌표" },
			{"pointx_15", "15번 측점 X좌표" },
			{"pointy_15", "15번 측점 Y좌표" },
			{"pointx_16", "16번 측점 X좌표" },
			{"pointy_16", "16번 측점 Y좌표" }
		};

		//View All_info_by_mesurepoints
		static Dictionary<string, string> all_infos = new Dictionary<string, string>
		{
			{"*","*" }
		};

		//measure_factors
		static Dictionary<string, string> measureFactors = new Dictionary<string, string>
		{
			{"id", "No" },
			{"area_code","장치 ID" },
			{"factor1","1번 오프셋" },
			{"factor2","2번 오프셋" },
			{"factor3","3번 오프셋" },
			{"factor4","4번 오프셋" },
			{"factor5","5번 오프셋" },
			{"factor6","6번 오프셋" },
			{"factor7","7번 오프셋" },
			{"factor8","8번 오프셋" },
			{"factor9","9번 오프셋" },
			{"factor10","10번 오프셋" },
			{"factor11","11번 오프셋" },
			{"factor12","12번 오프셋" },
			{"factor13","13번 오프셋" },
			{"factor14","14번 오프셋" },
			{"factor15","15번 오프셋" },
			{"factor16","16번 오프셋" },
			{"preset_num","프리셋 번호" }
		};

		static Dictionary<string, string> PresetInfos = new Dictionary<string, string>
		{
			{"id", "ID" },
			{"deviceid", "장치 ID"},
			{"preset_num", "프리셋 번호"},
			{"preset_top_level", "프리셋 상단레벨"},
			{"preset_bot_level", "프리셋 하단레벨"},
			{"top_px_width_offset", "상단 픽셀 너비 오프셋"},
			{"px_width_offset", "하단 픽셀 너비 오프셋"},
			{"px_ptz_mokjawidth", "하단 ptz 목자 너비 오프셋"},
			{"top_px_ptz_mokjawidth", "상단 ptz 목자 너비 오프셋"},
			{"measure_height", "목자판 픽셀 높이"},
			{"virtual_top_level", "화면상 최고 수위"},
			{"virtual_bot_level", "화면상 최저 수위"},
			{"riverstartloc", "측선 시작점"},
			{"riverendloc", "측선 끝점"},
			{"mokjapoints", "목자 위치"},
			{"virtual_mokjaimg_path", "가상 목자이미지 경로"},
		};

		//필드명 한글화
		private static string Set_queryFields(Dictionary<string, string> dict)
		{
			string fieldsStr = "";
			foreach (var field in dict)
			{
				fieldsStr += field.Key.ToString() + ", ";
			}
			fieldsStr = fieldsStr.Remove(fieldsStr.Length - 2);
			return fieldsStr;
		}

		// 입력필드 플레이스 홀더
		static Dictionary<string, string> placeHolders = new Dictionary<string, string>
		{
		{"krf_id", "10020113011" },
			{"observatorycode", "1002011301101" },
			{"name", "river" },
			{"placename", "○○교/○○천" },
			{"location","○○도 ○○시 ○○" },
			{"mokjatype","A" },
			{"locx","36.1234567" },
			{"locy","127.1234567" },
			{"admar_sicode", "12345" },
			{"areacode", "1234567890" },
			{"honeyrtsp","http://host:port" },
			{"axisrtsp","rtsp://id:password@host:port/profile2/media.smp" },
			{"httppath", "http://host:port/static/origin/No/" },
			{"sftppath", "/home/monwaters/server/uploads/crossSection/No/" },
			{"ftpid","monwaters" },
			{"ftppw","monwaters" },
			{"advisorylevel","000 (0.00M)" },
			{"warninglevel","000 (0.00M)" },
			{"riverwidth", "000 (000M)" },
			{"riverheight","00.00 (00.00EL.m)" },
			{"riverstartloc", "0000,0000" },
			{"riverendloc", "0000,0000" },
			{"wateraidetectport","0000" },
			{"dt","0.00"},
			{"mokjapoints", "입력은 메인 화면에서 해주세요." },
			{"mokjapoints_level_scale", "100"},
			{"measurecycle","0 (초)" },
			{"measuredigits","표기 자릿수"},
			{"virtual_top_level", "00.00 (00.00M)" },
			{"virtual_bot_level", "00.00 (00.00M)" },
			{"k_factor", "0.00" },
			{"thresh", "0.00" },
			{"measure_offset","0" },
			{"px_width_offset","0.00" },
			{"top_px_width_offset","0.00" },
			{"top_px_ptz_mokjawidth", "상단 ptz 목자 너비 오프셋"},
			{"px_ptz_mokjawidth", "하단 ptz 목자 너비 오프셋"},
			{"measure_height","0.00" },
			{"measure_angle", "0"},
			{"display_type", "표기 방식"},
			{"offset_j","0" },
			{"video_save_path", "/mnt/d/video/No/" },
			{"issiding", "유속측정방식"},
			{"piv_thresh_hold", "0.00" },
			{"detect_fps", "00" },
			{"stream_fps", "00" },
			{"jpeg_quality", "000" },
			{"use_virtual", "가상 모드" },
			{"auto_track", "수면 추적기능" },
			{"siding_direction", "측선 방향" },
			{"river_flow_direction", "하천 방향" },
			{"use_flow_direction", "하천 방향 사용" },
			{"use_surface_trace", "표면 추적 사용"},
			{"of_auto", "OF 자동" },
			{"gap_zero_deepest", "0.00" },
			{"waterai_save_quality", "100" },
			{"max_water_level", "0.1" },
			{"max_water_velocity", "0.1" },
			{"max_base_flow", "0.1" },
			{"flowrate_period", "5" },
			{"ptz_period", "5" },
			{"waterai_mode", "1" },
			{"license_id", "발급 후 입력"},

			//{"jjnethost","Detect Host"},
			//{"port","Detect 포트"},
			//{"username","Detect ID"},
			//{"password","Detect PW"},
			//{"detectaiserver_path", "Detect 저장소"},
			//{"monwatershost","MonWaters Host"},
			//{"monwatersport", "MonWaters 포트"},
			//{"monuname", "MonWaters ID"},
			//{"monpassword", "MonWaters PW"},
			//{"tensorflowpath","AIPIV 저장소"},
			//{"monwsport", "MonWatersWS 포트"},
			//{"sftpport","SFTP 포트"},
			//{"dbport", "DB 포트"},
			//{"myuname", "DB ID"},
			//{"mypassword", "DB PW"},
			//{"isblur", "블러 처리" },
			//{"ftpsavepath","FTP 저장소"},
			//{"obj_path","좌표파일 저장소"},
			{"fctapikey", "기상정보 필요시 입력" },
			{"fctapiurl", "기상청 API URL" },
			{"ndm_apikey", "발급후 입력" },
			{"ndm_apiurl", "http:/host:port/api/sriverObsOper/" },

			{"id", "ID"},
			{"deviceid", "장치 ID"},
			{"preset_num", "프리셋 번호"},
			{"preset_top_level", "프리셋 상단레벨"},
			{"preset_bot_level", "프리셋 하단레벨"},
			//{"top_px_width_offset", "상단 픽셀 너비 오프셋"},
			//{"px_width_offset", "하단 픽셀 너비 오프셋"},
			//{"top_px_ptz_mokjawidth", "상단 ptz 목자 너비 오프셋"},
			//{"px_ptz_mokjawidth", "하단 ptz 목자 너비 오프셋"},
			//{"measure_height", "목자판 픽셀 높이"},
			//{"virtual_top_level", "화면상 최고 수위"},
			//{"virtual_bot_level", "화면상 최저 수위"},
			//{"riverstartloc", "측선 시작점"},
			//{"riverendloc", "측선 끝점"},
			//{"mokjapoints", "목자 위치"},
			{"virtual_mokjaimg_path","가상 목자이미지 경로"}
		};

		private void Set_InputPanel(int rowIdx, int colIdx, int maxIdx)
		{
			List<string> mokjas = new List<string> { "H" };
			List<string> tfs = new List<string> { "True", "False" };
			List<string> rateTypes = new List<string> { "측선유속측정", "표면유속측정" };
			List<string> traceUse = new List<string>() { "수면 추적 사용", "수면 추적 사용 안함" };
			List<string> gateTypes = new List<string> { "자동", "수동" };
			List<string> digits = new List<string> { "0", "1", "2", "3" };
			List<string> displayTypes = new List<string> { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };//new List<string> { "1", "5", "6", "7", "8" };//new List<string> { "0", "1", "2", "3", "4", "5", "6", "7", "8" };
			List<string> directions = new List<string> { "하 -> 상", "상 -> 하" };
			List<string> river_directions = new List<string> { "우 -> 좌", "좌 -> 우", "전체" };
			List<string> wateraiModes = new List<string> { "1", "2", "3", "4" };

			try
			{
				if (input_panel.Controls.Count > 0)
				{
					input_panel.Controls.Clear();
				}
				TextBox virtual_top_level_tbox = new TextBox();
				TextBox virtual_bot_level_tbox = new TextBox();
				TextBox advisorylevel_tbox = new TextBox();
				TextBox warninglevel_tbox = new TextBox();
				if (rowIdx == maxIdx && tableQueryView.AllowUserToAddRows) //INSERT
				{
					for (int i = 1; i < colIdx; i++)
					{
						FlowLayoutPanel panel = new FlowLayoutPanel();
						Label label = new Label();
						TextBox textBox = new TextBox();
						ComboBox comboBox = new ComboBox();
						string en_field = tableQueryView.Columns[i].DataPropertyName;
						var table = tables[tableIdx];
						if (table.TryGetValue(en_field, out string kr_field))
						{
							label.Text = kr_field;
							label.Tag = en_field;
							textBox.Tag = kr_field;
							textBox.Text = placeHolders[en_field].ToString();
							textBox.ForeColor = Color.Gray;
							textBox.Enter += (sender, e) =>
							{
								if (textBox.ForeColor == Color.Gray)
								{
									textBox.Text = "";
									textBox.ForeColor = SystemColors.ControlText;
								}
							};
							textBox.Leave += (sender, e) =>
							{
								if (string.IsNullOrEmpty(textBox.Text))
								{
									textBox.Text = placeHolders[en_field];
									textBox.ForeColor = Color.Gray;
								}
							};
						}
						else
						{
							label.Text = en_field;
							textBox.ReadOnly = true;
							textBox.BackColor = SystemColors.ControlLightLight;
						}
						label.MinimumSize = new Size(150, 25);
						label.TextAlign = ContentAlignment.MiddleRight;
						textBox.MinimumSize = new Size(150, 25);
						textBox.TextAlign = HorizontalAlignment.Left;
						panel.Controls.Add(label);
						panel.Controls.Add(textBox);
						panel.FlowDirection = FlowDirection.LeftToRight;
						panel.AutoSize = true;
						comboBox.DropDownStyle = ComboBoxStyle.DropDownList;

						// 비밀번호
						if (en_field.Contains("pw") || en_field.Contains("password") || en_field.Contains("apikey"))
						{
							textBox.UseSystemPasswordChar = true;
						}
						//표기 자릿수
						if (en_field.Equals("measuredigits"))
						{
							textBox.Visible = false;
							foreach (string digit in digits)
							{
								comboBox.Items.Add(digit);
								comboBox.SelectedIndex = 0;
								comboBox.MinimumSize = new Size(150, 25);
							}
							panel.Controls.Add(comboBox);
						}
						//표기 방식
						if (en_field.Equals("display_type"))
						{
							textBox.Visible = false;
							foreach (string display in displayTypes)
							{
								comboBox.Items.Add(display);
								comboBox.SelectedIndex = 0;
								comboBox.MinimumSize = new Size(150, 25);
							}
							panel.Controls.Add(comboBox);
						}
						//waterai 모드
						if (en_field.Equals("waterai_mode"))
						{
							textBox.Visible = false;
							foreach (string wateraimode in wateraiModes)
							{
								comboBox.Items.Add(wateraimode);
								comboBox.SelectedIndex = 0;
								comboBox.MinimumSize = new Size(150, 25);
							}
							panel.Controls.Add(comboBox);
						}
						//목자타입
						if (en_field.Equals("mokjatype"))
						{
							textBox.Visible = false;
							foreach (string mokja in mokjas)
							{
								comboBox.Items.Add(mokja);
								comboBox.SelectedIndex = 0;
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedIndexChanged += (obj, args) =>
								{
									textBox.Text = comboBox.SelectedText;
								};
							}
							panel.Controls.Add(comboBox);
						}
						//True/False
						if (en_field.Equals("isblur") || en_field.Equals("use_virtual") || en_field.Equals("auto_track") || en_field.Equals("use_flow_direction") || en_field.Equals("of_auto"))
						{

							textBox.Visible = false;
							foreach (string tf in tfs)
							{
								comboBox.Items.Add(tf);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedIndex = 0;
							}
							panel.Controls.Add(comboBox);
						}
						//영상 저장소
						if (en_field.Equals("video_save_path"))
						{
							textBox.Click += (sender, e) =>
							{
								FolderBrowserDialog d = new FolderBrowserDialog();
								if (d.ShowDialog() == DialogResult.OK)
								{
									string selectPath = d.SelectedPath;
									selectPath = selectPath.Substring(0, 1).ToLower() + selectPath.Substring(1);
									selectPath = selectPath.Replace(":", "");
									string replacePath = selectPath.Replace("\\", "/");

									textBox.Text = "/mnt/" + replacePath + "/";
								}
							};
						}
						//유속측정방식
						if (en_field.Equals("issiding"))
						{
							textBox.Visible = false;
							foreach (string rateType in rateTypes)
							{
								comboBox.Items.Add(rateType);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedIndex = 0;
							}
							panel.Controls.Add(comboBox);
						}
						//차단기 상태
						if (en_field.Equals("gate_status"))
						{
							textBox.Visible = false;
							foreach (string gateType in gateTypes)
							{
								comboBox.Items.Add(gateType);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedIndex = 0;
							}
							panel.Controls.Add(comboBox);
						}
						// 측선 방향
						if (en_field.Equals("siding_direction"))
						{
							textBox.Visible = false;
							foreach (string direction in directions)
							{
								comboBox.Items.Add(direction);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedIndex = 0;
							}
							panel.Controls.Add(comboBox);
						}
						// 하천 방향
						if (en_field.Equals("river_flow_direction"))
						{
							textBox.Visible = false;
							foreach (string d in river_directions)
							{
								comboBox.Items.Add(d);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedIndex = 0;
							}
							panel.Controls.Add(comboBox);
						}
						// 표면 추적 사용
						if (en_field.Equals("use_surface_trace"))
						{
							textBox.Visible = false;
							foreach (string ust in traceUse)
							{
								comboBox.Items.Add(ust);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedIndex = 0;
							}
							panel.Controls.Add(comboBox);
						}
						// 화면상 최고 수위
						if (en_field.Equals("virtual_top_level"))
						{
							virtual_top_level_tbox = textBox;
						}
						// 화면상 최저 수위
						if (en_field.Equals("virtual_bot_level"))
						{
							virtual_bot_level_tbox = textBox;
						}
						// 주의보 수위
						if (en_field.Equals("advisorylevel"))
						{
							advisorylevel_tbox = textBox;
						}
						// 경보 수위
						if (en_field.Equals("warninglevel"))
						{
							warninglevel_tbox = textBox;
						}

						input_panel.Controls.Add(panel);
					}

					insertFlag = true;
				}
				else //UPDATE
				{
					DataGridViewRow selRow = tableQueryView.Rows[selIndex];
					DataRow row = (selRow.DataBoundItem as DataRowView).Row;
					rowIndex = row[0].ToString();
					for (int i = 1; i < colIdx; i++)
					{
						FlowLayoutPanel panel = new FlowLayoutPanel();
						Label label = new Label();
						TextBox textBox = new TextBox();
						ComboBox comboBox = new ComboBox();
						string en_field = tableQueryView.Columns[i].DataPropertyName;
						var table = tables[tableIdx];
						if (table.TryGetValue(en_field, out string kr_field))
						{
							label.Text = kr_field;
							label.Tag = en_field;
						}
						else
						{
							label.Text = en_field;
							textBox.Enabled = false;
							textBox.BackColor = SystemColors.ControlLightLight;
						}
						string text = row[en_field].ToString();
						label.MinimumSize = new Size(150, 25);
						label.TextAlign = ContentAlignment.MiddleRight;
						textBox.MinimumSize = new Size(150, 25);
						panel.Controls.Add(label);
						panel.Controls.Add(textBox);
						panel.FlowDirection = FlowDirection.LeftToRight;
						panel.AutoSize = true;
						comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
						//비밀번호
						if (en_field.Contains("pw") || en_field.Contains("password") || en_field.Contains("apikey"))
						{
							textBox.UseSystemPasswordChar = true;
							try
							{
								if (!en_field.Contains("apikey"))
								{
									text = new DetectWaterLevel.RsaEncDec().Decrypt(text, privateKey);
								}

								//textBox.Text = text;
							}
							catch (Exception ex)
							{
								Debug.WriteLine(ex.StackTrace);
							}
						}
						//표기 자릿수
						if (en_field.Equals("measuredigits"))
						{
							textBox.Visible = false;
							foreach (string digit in digits)
							{
								comboBox.Items.Add(digit);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedItem = (text == digit) ? digit : text;
							}
							panel.Controls.Add(comboBox);
						}
						//표기 방식
						if (en_field.Equals("display_type"))
						{
							textBox.Visible = false;
							foreach (string display in displayTypes)
							{
								comboBox.Items.Add(display);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedItem = (text == display) ? display : text;
							}
							panel.Controls.Add(comboBox);
						}
						//waterai 모드
						if (en_field.Equals("waterai_mode"))
						{
							textBox.Visible = false;
							foreach (string wateraimode in wateraiModes)
							{
								comboBox.Items.Add(wateraimode);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedItem = (text == wateraimode) ? wateraimode : text;
							}
							panel.Controls.Add(comboBox);
						}
						//목자타입
						if (en_field.Equals("mokjatype"))
						{

							textBox.Visible = false;
							foreach (string mokja in mokjas)
							{
								comboBox.Items.Add(mokja);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedItem = (text == mokja) ? mokja : text;
								comboBox.SelectedIndexChanged += (obj, args) =>
								{
									textBox.Text = comboBox.SelectedText;
								};
							}

							panel.Controls.Add(comboBox);
						}
						// 목자 위치
						/*
						if (en_field.Equals("mokjapoints"))
						{
							textBox.Enabled = false;
						}
						*/
						//True/False
						if (en_field.Equals("isblur") || en_field.Equals("use_virtual") || en_field.Equals("auto_track") || en_field.Equals("use_flow_direction"))
						{
							textBox.Visible = false;
							foreach (string tf in tfs)
							{
								comboBox.Items.Add(tf);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedItem = (text == "0") ? "False" : "True";
							}
							panel.Controls.Add(comboBox);
						}
						//OF 자동
						if (en_field.Equals("of_auto"))
						{
							textBox.Visible = false;
							foreach (string tf in tfs)
							{
								comboBox.Items.Add(tf);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedItem = (text == "F") ? "False" : "True";
							}
							panel.Controls.Add(comboBox);
						}
						//영상 저장소
						if (en_field.Equals("video_save_path"))
						{
							textBox.Click += (sender, e) =>
							{
								FolderBrowserDialog d = new FolderBrowserDialog();
								if (d.ShowDialog() == DialogResult.OK)
								{
									string selectPath = d.SelectedPath;
									selectPath = selectPath.Substring(0, 1).ToLower() + selectPath.Substring(1);
									selectPath = selectPath.Replace(":", "");
									string replacePath = selectPath.Replace("\\", "/");

									textBox.Text = "/mnt/" + replacePath;

								}
							};
						}
						//유속 측정방식
						if (en_field.Equals("issiding"))
						{
							textBox.Visible = false;
							foreach (string rateType in rateTypes)
							{
								comboBox.Items.Add(rateType);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedItem = (text == "T") ? "측선유속측정" : "표면유속측정";
							}
							panel.Controls.Add(comboBox);
						}
						//차단기 상태
						if (en_field.Equals("gate_status"))
						{
							textBox.Visible = false;
							foreach (string gateType in gateTypes)
							{
								comboBox.Items.Add(gateType);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedItem = (text == "0") ? "자동" : "수동";
							}
							panel.Controls.Add(comboBox);
						}
						//측선 방향
						if (en_field.Equals("siding_direction"))
						{
							textBox.Visible = false;
							foreach (string direction in directions)
							{
								comboBox.Items.Add(direction);
								comboBox.MinimumSize = new Size(150, 25);
								//comboBox.SelectedItem = (text == "0") ? "우안기준" : "좌안기준";
								comboBox.SelectedItem = (text == "0") ? "상 -> 하" : "하 -> 상";
							}
							panel.Controls.Add(comboBox);
						}
						// 하천 방향
						if (en_field.Equals("river_flow_direction"))
						{
							textBox.Visible = false;
							foreach (string d in river_directions)
							{
								comboBox.Items.Add(d);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedItem = (text == "0") ? "우 -> 좌" : (text == "1") ? "좌 -> 우" : "전체";
							}
							panel.Controls.Add(comboBox);
						}
						// 수면 추적 사용
						if (en_field.Equals("use_surface_trace"))
						{
							textBox.Visible = false;
							foreach (string ust in traceUse)
							{
								comboBox.Items.Add(ust);
								comboBox.MinimumSize = new Size(150, 25);
								comboBox.SelectedItem = (text == "0") ? "수면 추적 사용 안함" : "수면 추적 사용";
							}
							panel.Controls.Add(comboBox);
						}
						// 화면상 최고 수위
						if (en_field.Equals("virtual_top_level"))
						{
							virtual_top_level_tbox = textBox;
						}
						// 화면상 최저 수위
						if (en_field.Equals("virtual_bot_level"))
						{
							virtual_bot_level_tbox = textBox;
						}
						// 주의보 수위
						if (en_field.Equals("advisorylevel"))
						{
							advisorylevel_tbox = textBox;
						}
						// 경보 수위
						if (en_field.Equals("warninglevel"))
						{
							warninglevel_tbox = textBox;
						}

						if (text.Length > 0)
						{
							textBox.Text = text;
						}
						else
						{
							try
							{
								textBox.Text = placeHolders[en_field];
								textBox.ForeColor = Color.Gray;
								textBox.Enter += (sender, e) =>
								{
									if (textBox.ForeColor == Color.Gray)
									{
										textBox.Text = "";
										textBox.ForeColor = SystemColors.ControlText;
									}
								};
								textBox.Leave += (sender, e) =>
								{
									if (string.IsNullOrEmpty(textBox.Text))
									{
										textBox.Text = placeHolders[en_field];
										textBox.ForeColor = Color.Gray;
									}
								};
							}
							catch (Exception) { }
						}
						input_panel.Controls.Add(panel);
					}
					insertFlag = false;
					virtual_top_level_tbox.TextChanged += Virtual_top_level_TextChanged;
					virtual_bot_level_tbox.TextChanged += Virtual_top_level_TextChanged;
					void Virtual_top_level_TextChanged(object sender, EventArgs e)
					{
						try
						{
							String virtual_top_level_tbox_text = virtual_top_level_tbox.Text;
							String virtual_bot_level_tbox_text = virtual_bot_level_tbox.Text;
							float gap = float.Parse(virtual_top_level_tbox_text) - float.Parse(virtual_bot_level_tbox_text);
							advisorylevel_tbox.Text = Math.Round(float.Parse(virtual_bot_level_tbox_text) * 100 + gap * 50).ToString();
							warninglevel_tbox.Text = Math.Round(float.Parse(virtual_bot_level_tbox_text) * 100 + gap * 70).ToString();
						}
						catch (Exception) { }
					}
				}
				if (tableIdx != 4)
				{
					label1.Text = "※ 데이터를 입력한 뒤 저장버튼을 눌러 저장하세요.";
					saveBtn.Enabled = true;
				}
				else
				{
					label1.Text = "※ 종합 정보 조회시에는 값을 수정 할 수 없습니다.";
					saveBtn.Enabled = false;
				}
			}
			catch (Exception) { }
		}

		private IEnumerable<Control> GetAllControls(Control parent)
		{
			var controls = parent.Controls.Cast<Control>();
			return controls.SelectMany(ctrl => GetAllControls(ctrl)).Concat(controls);
		}

		private T FindControlByTagAndType<T>(Control parent, object tag) where T : Control
		{
			return GetAllControls(parent).OfType<T>().FirstOrDefault(c => c.Tag != null && c.Tag.Equals(tag));
		}

		// Query 결과값 입력창에 입력
		private Dictionary<string, string> Get_InputVal()
		{
			string value = "";
			string field = "";
			Dictionary<string, string> input = new Dictionary<string, string>();
			foreach (Control con in input_panel.Controls)
			{
				foreach (Control innerCon in con.Controls)
				{
					if (innerCon is Label label)
					{
						field = label.Tag.ToString();
					}
					if (innerCon is TextBox textBox)
					{
						if (textBox.ForeColor != Color.Gray)
						{
							value = textBox.Text;
						}
						else
						{
							value = "";
						}
						if (field.Contains("pw") || field.Contains("password"))
						{
							value = new DetectWaterLevel.RsaEncDec().Encrypt(value, publicKey);
						}
					}
					else if (innerCon is ComboBox comboBox)
					{
						try
						{
							value = comboBox.SelectedItem.ToString();
						}
						catch
						{
							value = comboBox.Text;
						}
						value = comboBox.SelectedItem.ToString();
						if (field.Equals("isblur") || field.Equals("video_save_mode") || field.Equals("use_virtual") || field.Equals("auto_track") || field.Equals("use_flow_direction"))
						{
							if (value == "True")
							{
								value = "1";
							}
							else if (value == "False")
							{
								value = "0";
							}
						}
						if (field.Equals("of_auto"))
						{
							if (value == "True")
							{
								value = "T";
							}
							else if (value == "False")
							{
								value = "F";
							}
						}
						if (field.Equals("issiding"))
						{
							if (value == "측선유속측정")
							{
								value = "T";
							}
							else if (value == "표면유속측정")
							{
								value = "F";
							}
						}
						if (field.Equals("siding_direction"))
						{
							if (value == "하 -> 상")
							{
								value = "1";
							}
							else if (value == "상 -> 하")
							{
								value = "0";
							}
						}
						if (field.Equals("river_flow_direction"))
						{
							if (value == "좌 -> 우")
							{
								value = "1";
							}
							else if (value == "우 -> 좌")
							{
								value = "0";
							}
							else if (value == "전체")
							{
								value = "2";
							}
						}
						if (field.Equals("use_surface_trace"))
						{
							if (value.Equals("수면 추적 사용"))
							{
								value = "1";
							}
							else if (value.Equals("수면 추적 사용 안함"))
							{
								value = "0";
							}
						}
					}
				}
				//if (!field.Contains("mokjapoints"))
				//{
				input.Add(field, value);
				//}
			}
			return input;
		}

		private void tableQueryView_CellClick(object sender, DataGridViewCellEventArgs e)
		{
			selIndex = tableQueryView.SelectedCells[0].RowIndex;
			int maxIdx = tableQueryView.RowCount - 1;
			int colIdx = tableQueryView.ColumnCount;
			if (maxIdx < 0)
			{
				maxIdx = 0;
			}
			//if (tableQueryView.AllowUserToAddRows)
			//									{
			//													maxIdx++;
			//									}
			Set_InputPanel(selIndex, colIdx, maxIdx);
		}

		//DataGridView Row 삭제
		private void tableQueryView_UserDeletingRow(object sender, DataGridViewRowCancelEventArgs e)
		{
			DialogResult res = MessageBox.Show("선택한 데이터를 삭제합니다. 이 후 복구는 불가능 합니다.\n삭제하시겠습니까?", null, MessageBoxButtons.YesNo);
			if (res == DialogResult.Yes)
			{
				try
				{
					string noValue = tableQueryView.Rows[selIndex].Cells[0].Value.ToString();
					string deleteQuery = $"DELETE FROM {tableName} WHERE id={noValue};";
					using (NpgsqlConnection conn = new NpgsqlConnection(_psqlConnection))
					{
						conn.Open();
						NpgsqlCommand comm = new NpgsqlCommand(deleteQuery, conn);
						comm.ExecuteNonQuery();
						conn.Close();
					}
					input_panel.Controls.Clear();
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.ToString());
				}
			}
			else if (res == DialogResult.No)
			{
				e.Cancel = true;
			}
		}

		//DB접속 정보 입력 폼 생성
		private void toolStripMenuItem1_Click(object sender, EventArgs e)
		{
			if (privateKey != null)
			{
				InsertInfoForm insertInfoForm = new InsertInfoForm();
				insertInfoForm.ShowDialog();
			}
			else
			{
				MessageBox.Show("암호 식별자가 없습니다. \n암호 식별자 입력을 통해 암호 식별자를 입력해주세요.", "암호 식별자 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		//RSA 암복호화 Key 확인
		public void CheckKeys()
		{
			try
			{
				using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
				{
					if (File.Exists(privateKeyPath))
					{
						privateKey = File.ReadAllText(privateKeyPath);
					}
					else
					{
						MessageBox.Show("암호 식별자가 없습니다. \n암호 식별자 입력을 통해 암호 식별자를 입력해주세요.", "암호 식별자 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
					}

					if (File.Exists(publicKeyPath))
					{
						publicKey = File.ReadAllText(publicKeyPath);
					}
				}
			}
			catch (Exception)
			{
			}
		}

		private void insert_privateKey_Click(object sender, EventArgs e)
		{
			getKeyFile();
		}

		// PrivateKey 불러오기
		private void getKeyFile()
		{
			OpenFileDialog openFileDialog = new OpenFileDialog();

			openFileDialog.Title = "암호 식별자입력";
			openFileDialog.Filter = "Xml 파일 (*.xml)|*.xml|모든 파일 (*.*)|*.*";
			openFileDialog.Multiselect = false;

			if (openFileDialog.ShowDialog() == DialogResult.OK)
			{
				try
				{
					privateKey = openFileDialog.FileName;
					string a = File.ReadAllText(privateKey);
					privateKey = a;
					File.WriteAllText(privateKeyPath, privateKey);
					MessageBox.Show("암호 식별자가 입력되었습니다.", "암호 식별자 입력 성공", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				catch
				{
					MessageBox.Show("암호 식별자 입력에 실패했습니다.", "암호 식별자 입력 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		// 키 저장소 확인
		private void checkKeyDir()
		{
			string keyDir = $@"C:\Users\{Environment.UserName}\.monwaters";
			if (!Directory.Exists(keyDir))
			{
				Directory.CreateDirectory(keyDir);
			}
			else
			{
				CheckKeys();
			}
		}

		//장치 목록 불러오기
		private void GetDeviceList()
		{
			checkSection.DropDownItems.Clear();
			try
			{
				string query = $"SELECT id, placeName FROM device WHERE  isonair = \'1\' ORDER BY id ASC";
				Dictionary<int, string> map = new Dictionary<int, string>();
				using (NpgsqlConnection conn = new NpgsqlConnection(_psqlConnection))
				{
					conn.Open();
					using (NpgsqlCommand cmd = new NpgsqlCommand(query, conn))
					{
						using (NpgsqlDataReader reader = cmd.ExecuteReader())
						{
							while (reader.Read())
							{
								int id = Convert.ToInt32(reader["id"]);
								string placeName = (string)reader["placeName"];
								Console.WriteLine("[{0},{1}]", id, placeName);
								map.Add(id, placeName);
							}
							reader.Close();
						}
					}
					conn.Close();
				}
				foreach (var item in map)
				{
					ToolStripMenuItem placeItem = new ToolStripMenuItem(item.Value);
					ToolStripMenuItem subItem1 = new ToolStripMenuItem("단면적 확인");
					subItem1.Click += (sender, e) =>
					{
						Datapanel.Visible = false;
						Crosssectionpanel.Visible = true;
						selectDevice = item.Key;
						var host = GetCrossSectionInfos();
						int result = CrossSectionStream(host);
						switch (result)
						{
							case 0:
								MessageBox.Show("파일 다운로드에 실패했습니다. \n기본설정의 MonWatersServer 접속정보를 확인해주세요.", "파일 다운로드 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
								break;
							case 1:
								MessageBox.Show("이미지가 존재하지 않습니다.", "", MessageBoxButtons.OK);
								break;
							case 2:
								break;
						}
					};
					subItem1.ToolTipText = "하천의 단면 계측 정보를 표기합니다.";
					ToolStripMenuItem subItem2 = new ToolStripMenuItem("좌표파일 저장");
					subItem2.ToolTipText = "하천의 단면적 좌표파일을 저장합니다.";
					subItem2.Click += (sender, e) =>
					{
						selectDevice = item.Key;
						var host = GetObj_dataInfos();
						InsertObj_dataStream(host);
					};
					placeItem.DropDownItems.Add(subItem1);
					placeItem.DropDownItems.Add(subItem2);
					checkSection.DropDownItems.Add(placeItem);
				}

			}
			catch (Exception ex) { Console.WriteLine(ex.Message); }
		}
		/* privateKey 생성
					private void createKey() {
			using(var rsa = new RSACryptoServiceProvider())
			{
				privateKey = rsa.ToXmlString(true);
				File.WriteAllText(privateKeyPath, privateKey);
				publicKey = rsa.ToXmlString(false);
				File.WriteAllText(publicKeyPath, publicKey);
			}
					}
					*/

		//obj_path Query
		private SFTPHost GetObj_dataInfos()
		{
			try
			{
				sftphost = new SFTPHost();
				using (NpgsqlConnection conn = new NpgsqlConnection(_psqlConnection))
				{
					conn.Open();
					string select = $"SELECT s.jjnethost, s.sftpport, s.username, s.password, s.obj_path FROM settings s";
					using (NpgsqlCommand cmd = new NpgsqlCommand(select, conn))
					{
						using (NpgsqlDataReader reader = cmd.ExecuteReader())
						{
							while (reader.Read())
							{
								sftphost.Host = (string)reader["jjnethost"];
								sftphost.Port = (int)(Int64)reader["sftpport"];
								sftphost.UserName = (string)reader["username"];
								string pw = (string)reader["password"];
								string encPw = new DetectWaterLevel.RsaEncDec().Decrypt(pw, privateKey);
								sftphost.Password = encPw;
								sftphost.Path = (string)reader["obj_path"];
							}
							reader.Close();
						}
					}
					conn.Close();
				}
			}
			catch (Exception ex) { Debug.WriteLine(ex.StackTrace); }
			return sftphost;
		}

		//단면적 저장 위치 Query
		public SFTPHost GetCrossSectionInfos()
		{
			try
			{
				sftphost = new SFTPHost();
				using (NpgsqlConnection conn = new NpgsqlConnection(_psqlConnection))
				{
					conn.Open();
					string selectQuery = $"SELECT s.monwatershost, s.monwatersport, s.monuname, s.monpassword, d.sftppath FROM device d, settings s where d.id={selectDevice}";
					using (NpgsqlCommand comm = new NpgsqlCommand(selectQuery, conn))
					{
						using (NpgsqlDataReader reader = comm.ExecuteReader())
						{
							while (reader.Read())
							{
								sftphost.Host = (string)reader["monwatershost"];
								sftphost.Port = (int)(Int64)reader["monwatersport"];
								sftphost.UserName = (string)reader["monuname"];
								string pw = (string)reader["monpassword"];
								string encPw = new DetectWaterLevel.RsaEncDec().Decrypt(pw, privateKey);
								sftphost.Password = encPw;
								sftphost.Path = (string)reader["sftppath"];
							}
							reader.Close();
						}
					}
					conn.Close();
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
			}
			return sftphost;
		}

		public void InsertObj_dataStream(SFTPHost host)
		{
			try
			{
				OpenFileDialog openFileDialog = new OpenFileDialog();
				if (openFileDialog.ShowDialog() == DialogResult.OK)
				{
					string localFilePath = openFileDialog.FileName;
					if (host == null)
					{
						host = GetObj_dataInfos();
					}
					string saveDir = host.Path;
					using (SftpClient sftp = new SftpClient(host.Host, host.Port, host.UserName, host.Password))
					{
						sftp.Connect();
						if (sftp.IsConnected)
						{
							sftp.ChangeDirectory(saveDir);
							using (var fileStream = new FileStream(localFilePath, FileMode.Open))
							{
								sftp.BufferSize = 4 * 1024;
								sftp.UploadFile(fileStream, selectDevice.ToString() + ".txt");

							}
							sftp.Disconnect();
						}
						string uploadedFilePath = host.Path + selectDevice.ToString() + ".txt";
						Debug.WriteLine(uploadedFilePath);
						UpdateDeviceObjPath(uploadedFilePath);
					}
					MessageBox.Show("단면 좌표 저장이 완료 되었습니다.", "파일 업로드 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
					QueryTable();
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("파일 업로드에 실패했습니다. \n기본설정의 DetectAIServer 접속정보를 확인해주세요.", "파일 업로드 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Debug.WriteLine(ex.StackTrace);
			}
		}

		public void UpdateDeviceObjPath(string filepath)
		{
			try
			{
				using (var conn = new NpgsqlConnection(_psqlConnection))
				{
					conn.Open();
					string query = $"UPDATE device SET flowamountweightpath=\'{filepath}\' WHERE id = {selectDevice}";
					using (var cmd = new NpgsqlCommand(query, conn))
					{
						cmd.ExecuteNonQuery();
					}
					conn.Close();
				}
			}
			catch (Exception) { }
		}

		// 단면적 이미지 불러오기
		public int CrossSectionStream(SFTPHost host)
		{
			try
			{
				if (host == null)
				{
					host = GetCrossSectionInfos();
				}
				string imgDir = host.Path;
				using (SftpClient sftp = new SftpClient(host.Host, host.Port, host.UserName, host.Password))
				{
					sftp.Connect();
					sftp.ChangeDirectory(imgDir);

					SftpFile lastestFile = sftp.ListDirectory(imgDir)
					.Where(file => !file.IsDirectory && file.Name.EndsWith(".png"))
					.OrderByDescending(file => file.LastWriteTime)
					.FirstOrDefault();

					if (lastestFile == null)
					{
						crossSectionPic.Image = null;
						return 1;
					}
					using (var stream = sftp.OpenRead(lastestFile.FullName))
					{
						System.Drawing.Image image = System.Drawing.Image.FromStream(stream);
						crossSectionPic.Image = image;
					}
					sftp.Disconnect();
				}
				return 2;
			}
			catch (Exception)
			{
				crossSectionPic.Image = null;
				return 0;
			}
		}

		// 단면적 이미지에 정보 그리기
		private void crossSectionPic_Paint(object sender, PaintEventArgs e)
		{
			if (crossSectionPic.Image == null) return;

			Dictionary<Color, string> colors = new Dictionary<Color, string> { { Color.Red, "측선" }, { Color.Blue, "윤변" }, { Color.Green, "수심" }, { Color.Chartreuse, "단면적" } };
			Font font = new Font("Arial", 10);
			var x = crossSectionPic.Width - 100;
			var y = 20;
			int i = 0;

			foreach (var color in colors)
			{
				var brush = new SolidBrush(color.Key);
				var rectX = x;
				var rectY = y + (i * 30);
				var rectWidth = 15;
				var rectHeight = 15;

				e.Graphics.FillRectangle(brush, rectX, rectY, rectWidth, rectHeight);
				e.Graphics.DrawString(color.Value, font, Brushes.Black, x + 20, rectY);
				i++;
			}
		}

		private void listBox_tables_ValueMemberChanged(object sender, EventArgs e)
		{
			tableName = listBox_tables.SelectedValue.ToString();
			tableIdx = listBox_tables.SelectedIndex;
			QueryTable();
		}

		private void tableQueryView_DataSourceChanged(object sender, EventArgs e)
		{
			int maxIdx = tableQueryView.RowCount - 1;
			int colIdx = tableQueryView.ColumnCount;

			// 이전에 선택된 인덱스를 유지합니다.
			if (previousSelectedIndex >= 0 && previousSelectedIndex <= maxIdx)
			{
				selIndex = previousSelectedIndex;
				tableQueryView.ClearSelection();
				tableQueryView.Rows[selIndex].Selected = true;
			}
			else
			{
				selIndex = 0;
				tableQueryView.Rows[selIndex].Selected = true;
			}

			Set_InputPanel(selIndex, colIdx, maxIdx);
		}

		//입력값 유효성 검사
		private bool Check_InputValue()
		{
			try
			{
				var inputs = input_panel.Controls;
				Regex rx = new Regex(@"[a-zA-Z0-9가-힣\W]*$");
				string name = "";
				string value;
				bool res = true;
				//Regex pathRx = new Regex(@"^(((rtsp|https?|sftp):\/\/)(([\w]+):([\S]+@))?(((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)|[\w]+):([\d]+)([\S\/]+?)){1,100}$");
				Regex pathRx = new Regex(@"^(rtsp|https?|sftp):\/\/(?:([^:\/]+):([^@]+)@)?([^:\/]+)(?::(\d+))?(\/[^\s]*)?$");
				foreach (Control con in inputs)
				{
					foreach (Control input in con.Controls)
					{
						value = "";
						if (input is Label label)
						{
							name = input.Text.ToString();
							if (name.Contains("번 오프셋"))
							{
								rx = new Regex(@"(^(\d{1}|10)$)|(^\d{0,2}.\d{1,5}$)");
							}
							if (name.Contains("측점"))
							{
								rx = new Regex(@"^(\d{1,8}$)|(^\d{1,8}.\d{1,2}$)");
							}
							if (name.Contains("포트"))
							{
								rx = new Regex(@"^([1-9]([\d]{3,4}))*$");
							}
							if (name.Equals("차단기 포트이름"))
							{
								rx = new Regex(@"^(COM([1-9]))*$");
							}
							switch (name)
							{
								case "주소":
									rx = new Regex(@"^[가-힣0-9\s]*$");
									break;
								case "IP 주소":
								case "Detect Host":
								case "MonWaters Host":
									rx = new Regex(@"^(((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)|[a-zA-z0-9]+)");
									break;
								case "목자판 타입":
									rx = new Regex(@"^(H)*$");
									break;
								case "CCTV ID":
								case "Detect ID":
								case "FTP ID":
								case "MonWaters ID":
								case "DB ID":
								case "ID":
									rx = new Regex(@"^^[A-Za-z0-9]{1,20}$");
									break;
								case "CCTV PW":
								case "Detect PW":
								case "FTP PW":
								case "MonWaters PW":
								case "DB PW":
								case "PW":
									rx = new Regex(@"([a-zA-Z0-9\W]+){1,20}$");
									break;
								case "기상청 API Key":
									rx = new Regex(@"([a-zA-Z0-9\W]+){0,20}$");
									break;
								case "HTTP 주소":
								case "스트리밍 주소":
								case "원본 저장소":
								case "계측 저장소":
								case "CCTV 주소":
									rx = pathRx;
									break;
								case "측선 시작점":
								case "측선 끝점":
									rx = new Regex(@"(^[\s]*$|^\s*(0|[1-9]\d{0,2}|1[0-8]\d{0,2}|19[01]\d|1920)\s*,\s*(([0-9]|[1-3]\d|4[0-5])|(103[5-9]|10[4-7]\d|1080))\s*$)");
									break;
								case "목자 위치":
									rx = new Regex(@"(^.*)");
									break;
								case "FTP 저장소":
								case "단면적 저장소":
									rx = new Regex(@"^\.\/([\S]+)*$");
									break;
								case "단면 이미지 저장소":
								case "AIPIV 저장소":
								case "유량 파일 저장소":
								case "좌표파일 저장소":
								case "영상 저장소":
									rx = new Regex(@"^((\/[\S]+)|\s){1,100}$");
									break;
								case "표시 상태":
								case "정비 상태":
								case "블러 처리":
								case "자동 저장 모드":
								case "방송 상태":
								case "차단기 모드":
								case "가상 모드":
								case "수면 추적기능":
								case "측선 방향":
								case "하천 방향 사용":
									rx = new Regex(@"^[0-1]*$");
									break;
								case "유속측정방식":
								case "OF 자동":
									rx = new Regex(@"^[TF]*$");
									break;
								case "시 코드번호":
								case "지역 코드":
								case "주의보 수위":
								case "경보 수위":
								case "목자 수위 크기":
									rx = new Regex(@"^[0-9]{1,10}$");
									break;
								case "표기 방식":
								case "waterai 모드":
									rx = new Regex(@"^[0-9]*$");
									break;
								case "촬영 각도":
								case "수위 오프셋":
									rx = new Regex(@"^-?[0-9]{1,10}$");
									break;
								case "상단 픽셀 너비 오프셋":
								case "하단 픽셀 너비 오프셋":
									rx = new Regex(@"^(0+)?(1920(\.0{0,2})?|19[0-1]\d(\.\d{0,2})?|1[0-8]\d{2}(\.\d{0,2})?|\d{0,3}(\.\d{0,2})?)$");
									break;
								case "목자판 픽셀 높이":
									rx = new Regex(@"^(0+)?((1080(\.0{0,2})?)|(10[0-7]\d(\.\d{0,2})?)|(\d{0,3}(\.\d{0,2})?))$");
									break;
								case "구 코드번호":
									rx = new Regex(@"^[0-9\s]*$");
									break;
								case "이름":
									rx = new Regex(@"^([a-zA-z0-9]){1,20}$");
									break;
								case "하천명":
									rx = new Regex(@"^([가-힣0-9]){1,10}$");
									break;
								case "화면상 최고 수위":
								case "화면상 최저 수위":
								case "PIV 한계값":
								case "하천 폭":
								case "영점표고(EL.m)":
								case "측정 오프셋":
								case "수심 영점":
								case "최대 수위":
								case "최대 유속":
								case "최대 기본 수위":
									rx = new Regex(@"(^-?\d{1,7}$)|(^-?\d{1,7}.\d{1,3}$)");
									break;
								case "기상청 API URL":
									rx = new Regex(@"^http:\/\/([\S]+){1,100}$");
									break;
								case "차단기 열림 명령어":
								case "차단기 닫힘 명령어":
								case "차단기 열림 완료 명령어":
								case "차단기 닫힘 완료 명령어":
									rx = new Regex(@"(([0-9a-zA-Z]\s)+)*$");
									break;
								case "PIV 측정 주기":
								case "PTZ 변경 주기":
									rx = new Regex(@"^(?:[1-9]|[1-5][0-9]|60)$");
									break;
								case "계측 FPS":
								case "스트리밍 FPS":
									rx = new Regex(@"^(?:[1-9]|[12]\d|30)?$");
									break;
								case "저장 퀄리티":
								case "프레임 퀄리티":
									rx = new Regex(@"^(?:[1-9]\d?|100)?$");
									break;
								case "소하천코드번호":
									rx = new Regex(@"^[0-9]{11}$");
									; break;
								case "관측소 코드":
									rx = new Regex(@"^[0-9]{13}$");
									break;
								case "위도":
								case "경도":
									rx = new Regex(@"(^(\d{1,3})$)|(^\d{1,3}.\d{1,7}$)");
									break;
								case "DT":
									rx = new Regex(@"(^(\d)*$)|(^\d.\d{1,4}$)");
									break;
								case "K-Factor":
								case "한계값":
									rx = new Regex(@"(^(\d{1,2})$)|(^\d{1,2}.\d{1,3}$)");
									break;
								case "계측 주기":
								case "표기 자릿수":
									rx = new Regex(@"([0-9])*$");
									break;
								case "라이선스":
									rx = new Regex(@"^[A-Za-z0-9]{0,250}$");
									break;
								case "국립재난안전연구원 API Key":
									rx = new Regex(@"([a-zA-Z0-9\W]+){0,20}$");
									break;
								case "국립재난안전연구원 API URL":
									rx = new Regex(@"^([\S]+){0,100}$");
									break;
								case "하천 방향":
									rx = new Regex(@"[0-2]*$");
									break;

									//default:
									//	rx = new Regex(@"^([\s]*$|([a-zA-Z0-9가-힣\W])*$)");
									//	break;
							}
							continue;
						}
						else if (input is TextBox textBox)
						{
							if (input.ForeColor != Color.Gray)
							{
								value = input.Text.ToString();
							}
							else
							{
								value = "";
							}
						}
						res = rx.IsMatch(value);
						if (!res)
						{
							var r = MessageBox.Show($"{name}의 입력값이 규칙에 맞지 않습니다. 다시 입력해주세요.", null, MessageBoxButtons.OK);
							if (r == DialogResult.OK)
							{
								return res;
							}
						}
					}
				}
				return res;
			}
			catch (Exception)
			{
				return false;
			}
		}

		//고유키 유무 확인
		private void Check_identifier_key(object sender, EventArgs e)
		{
			Open_identifireForm();
		}

		//고유키 확인 Form 생성
		private void Open_identifireForm()
		{
			IdentifierForm idfForm = new IdentifierForm();
			idfForm.ShowDialog();
		}

		private void Open_AppInfoForm()
		{
			AppInfoForm infoForm = new AppInfoForm();
			infoForm.ShowDialog();
		}

		// 메뉴얼 다운로드
		private void DownLoad_AppGuide()
		{
			Assembly assembly = Assembly.GetExecutingAssembly();
			AssemblyCopyrightAttribute copyattr = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
			if (copyattr != null)
			{
				copystr = copyattr.Copyright;
			}
			using (SaveFileDialog saveFileDialog = new SaveFileDialog())
			{
				saveFileDialog.Title = "다운로드 할 위치를 선택하세요.";
				saveFileDialog.FileName = AppNameStr + " V " + AppVer + "_사용자취급설명서.pdf";
				saveFileDialog.Filter = "모든 파일(*.*)|*.*";

				if (saveFileDialog.ShowDialog() == DialogResult.OK)
				{
					string downloadURL = "file:/C:\\Program Files (x86)\\MonWatersAdmin\\WaterAI V1.0_사용자취급설명서.pdf";
					string downloadPath = saveFileDialog.FileName;

					using (WebClient client = new WebClient())
					{
						try
						{
							client.DownloadFile(downloadURL, downloadPath);
							MessageBox.Show("파일 다운로드가 완료되었습니다.", "파일 다운로드 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
						}
						catch (Exception)
						{
							MessageBox.Show("파일 다운로드에 실패했습니다.", "파일 다운로드 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
						}
					}

				}
			}
		}

		// 고유키 Query
		public DataTable Select_identifier()
		{
			string qry = $"select placename, identifier_key from device";
			DataTable dt = new DataTable();
			try
			{
				using (NpgsqlConnection conn = new NpgsqlConnection(_psqlConnection))
				{
					conn.Open();
					using (NpgsqlDataAdapter adapter = new NpgsqlDataAdapter(qry, conn))
					{
						adapter.Fill(dt);
					}
					conn.Close();
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.StackTrace);
			}
			return dt;
		}

		// 라이선스 키 유무 확인
		private bool Check_License_Key()
		{
			string qry = $"SELECT license_id from device";

			bool license = true;
			try
			{
				using (NpgsqlConnection conn = new NpgsqlConnection(_psqlConnection))
				{
					conn.Open();
					using (NpgsqlCommand cmd = new NpgsqlCommand(qry, conn))
					{
						using (NpgsqlDataReader reader = cmd.ExecuteReader())
						{
							while (reader.Read())
							{
								string lnc = reader["license_id"].ToString();
								if (lnc.Length > 0)
								{
									license = true;
								}
								else
								{
									license = false;
								}
							}
							reader.Close();
						}
					}
					conn.Close();
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.StackTrace);
			}
			return license;
		}

		// DB 쿼리
		private DataTable GetData(double offset)
		{
			return GetData(offset, null);
		}

		private DataTable GetData(double offset, BackgroundWorker bg_worker)
		{
			try
			{
				conn = new NpgsqlConnection(_psqlConnection);
				if (bg_worker != null && bg_worker.CancellationPending)
				{
					return null;
				}
				conn.Open();
				if (bg_worker != null && bg_worker.CancellationPending)
				{
					return null;

				}
				//string query = $"SELECT {queryFields[tableIdx]} FROM {tableName} Offset {offset} Limit {pageSize}";
				string query;

				if (tableIdx == 0) // device를 쿼리할 때
				{
					query = $"SELECT {queryFields[tableIdx]} FROM {tableName} ORDER BY id OFFSET {offset} LIMIT {pageSize}";
				}

				else if (tableIdx == 6) // PresetInfos를 쿼리할 때
				{
					query = $"SELECT {queryFields[tableIdx]} FROM {tableName} ORDER BY preset_num OFFSET {offset} LIMIT {pageSize}";
				}
				else
				{
					query = $"SELECT {queryFields[tableIdx]} FROM {tableName} OFFSET {offset} LIMIT {pageSize}";
				}

				DataTable dt = new DataTable();
				if (bg_worker != null && bg_worker.CancellationPending)
				{
					return null;
				}
				try
				{
					adapter = new NpgsqlDataAdapter(query, conn);
					adapter.Fill(dt);
				}
				catch (Exception)
				{

					if (bg_worker != null && bg_worker.CancellationPending)
					{
						return null;
					}
				}

				count = GetCount(conn);
				conn.Close();
				if (bg_worker != null && bg_worker.CancellationPending)
				{
					return null;
				}
				if (pageFlag)
				{
					nowCnt++;
				}
				else
				{
					nowCnt--;
				}
				CrossThreadLabel(label_warn2, nowCnt, count);

				return dt;
			}
			catch (Exception)
			{
				return null;
			}
		}

		public void CrossThreadLabel(Control item, double nowCnt, int cnt)
		{
			if (item.InvokeRequired)
			{
				item.BeginInvoke(new MethodInvoker(delegate ()
				{
					CrossThreadLabel(item, nowCnt, cnt);
				}));
				return;
			}
			item.Text = $"{nowCnt}/{cnt}";
			item.Dock = DockStyle.Right;
		}

		// 쿼리 결과 페이징
		private int GetCount(NpgsqlConnection conn)
		{
			double count;
			try
			{
				string qurey = $"SELECT count(1) FROM {tableName}";
				using (var comm = new NpgsqlCommand(qurey, conn))
				{
					double cnt = Convert.ToInt32(comm.ExecuteScalar());
					if (cnt > pageSize)
					{
						count = cnt / pageSize;
						count = Math.Ceiling(count);
					}
					else
					{
						count = 1;
					}
				}
				return (int)count;
			}
			catch (NpgsqlException)
			{
				return this.count;
			}
			catch (Exception)
			{
				return this.count;
			}
		}

		// 다음 페이지
		private void next_btn_Click(object sender, EventArgs e)
		{
			try
			{
				if (nowCnt < count)
				{
					pageFlag = true;
					pg_form = new ProgressForm(this);
					pg_form.Show();
					bg_worker = new BackgroundWorker();
					pg_form.SetBgWorker(bg_worker);

					bg_worker.DoWork += (obj, args) =>
					{
						offset += pageSize;
						DataTable dt = GetData(offset, bg_worker);
						if (!bg_worker.CancellationPending)
						{
							Init_DGV(dt);
						}
						//QueryTable();
					};
					bg_worker.RunWorkerCompleted += (obj, args) =>
					{
						if (!bg_worker.CancellationPending)
						{
							pg_form.Close();
							Datapanel.Visible = true;
							Crosssectionpanel.Visible = false;
						}
						if (args.Cancelled)
						{
							Datapanel.Visible = true;
							Crosssectionpanel.Visible = false;
						}
					};
					bg_worker.WorkerSupportsCancellation = true;
					bg_worker.RunWorkerAsync();
				}
				else
				{
					MessageBox.Show("가장 마지막 페이지 입니다.");
				}
			}
			catch (Exception)
			{
			}
		}

		// 이전 페이지
		private void prev_btn_Click(object sender, EventArgs e)
		{
			try
			{
				if (nowCnt > 1)
				{
					pageFlag = false;
					pg_form = new ProgressForm(this);
					pg_form.Show();
					bg_worker = new BackgroundWorker();
					pg_form.SetBgWorker(bg_worker);

					bg_worker.DoWork += (obj, args) =>
					{
						offset -= pageSize;
						DataTable dt = GetData(offset, bg_worker);
						if (!bg_worker.CancellationPending)
							Init_DGV(dt);
						//QueryTable();
					};
					bg_worker.RunWorkerCompleted += (obj, args) =>
					{
						if (!bg_worker.CancellationPending)
						{
							pg_form.Close();
							Datapanel.Visible = true;
							Crosssectionpanel.Visible = false;
						}
						if (args.Cancelled)
						{
							Datapanel.Visible = true;
							Crosssectionpanel.Visible = false;
						}
					};
					bg_worker.WorkerSupportsCancellation = true;
					bg_worker.RunWorkerAsync();
				}
				else
				{
					MessageBox.Show("가장 첫번째 페이지 입니다.");
				}

			}
			catch (Exception)
			{

			}
		}

		// DateGridView 초기화
		private void Init_DGV(DataTable dt)
		{
			if (tableQueryView.InvokeRequired)
			{
				tableQueryView.BeginInvoke((Action)delegate
				{
					Init_DGV(dt);
				});
				return;
			}
			try
			{
				if (tableIdx == 0 || tableIdx == 6)
				{
					if (dt.Rows.Count > 0)
					{
						tableQueryView.AllowUserToDeleteRows = true;
						tableQueryView.AllowUserToAddRows = false;
					}
					else if (dt.Rows.Count == 0)
					{
						tableQueryView.AllowUserToDeleteRows = false;
						tableQueryView.AllowUserToAddRows = true;
					}
				}
				else
				{
					tableQueryView.AllowUserToAddRows = false;
					tableQueryView.AllowUserToDeleteRows = false;
				}

				tableQueryView.MultiSelect = false;
				tableQueryView.DataSource = dt;

				if (tableIdx != 4)
				{
					prev_btn.Visible = false;
					next_btn.Visible = false;
					label_warn2.Visible = false;
				}
				else
				{
					prev_btn.Visible = true;
					next_btn.Visible = true;
					label_warn2.Visible = true;
				}
			}
			catch (Exception)
			{
				//if (tableName == "device " || tableName == "measure_factors" || tableName == " device" || tableName == "all_info_query")
				//{
				//	MessageBox.Show("데이터가 존재하지 않습니다. 장치 관리에서 장치를 등록해주세요.", "데이터 없음 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
				//}
			}
		}

		//XML 파일 읽어 설정 목록 생성
		private void Set_ListBox_tables()
		{
			string appDir = Assembly.GetExecutingAssembly().Location;
			string appInstallDir = Path.GetDirectoryName(appDir);
			XmlDocument xDoc = new XmlDocument();
			xDoc.Load($@"{appInstallDir}\listBox.xml");
			XmlNodeList nodes = xDoc.SelectNodes("/items/item");
			foreach (XmlNode node in nodes)
			{
				string strId = node.SelectSingleNode("id")?.InnerText;
				int.TryParse(strId, out int val);
				int id = val;
				string name = node.SelectSingleNode("name")?.InnerText;
				string type = node.SelectSingleNode("type")?.InnerText;
				string value = node.SelectSingleNode("value")?.InnerText;
				string addrow = node.SelectSingleNode("addrow")?.InnerText;

				if (!string.IsNullOrEmpty(name) & !string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(value))
				{
					listBox_tables.Items.Add(new XmlData(id, name, type, value, addrow));
				}
			}
			listBox_tables.SelectedIndexChanged += Set_ListBox_tables_event;
			listBox_tables.SelectedIndex = 0;
		}

		//설정 목록 타입에 따른 클릭 이벤트
		private void Set_ListBox_tables_event(object sender, EventArgs e)
		{
			if (listBox_tables.SelectedItem != null)
			{
				XmlData selectedItem = (XmlData)listBox_tables.SelectedItem;

				switch (selectedItem.Type)
				{
					case "query": // DB 쿼리
						try
						{
							tableName = selectedItem.Value;
							tableIdx = listBox_tables.SelectedIndex;
							pageFlag = true;
							nowCnt = 0;
							offset = 0;

							pg_form = new ProgressForm(this);
							bg_worker = new BackgroundWorker();
							pg_form.SetBgWorker(bg_worker);

							if (!activateFormExist)
							{
								activateFormExist = true;
								pg_form.Show();
							}

							bg_worker.DoWork += (obj, args) =>
							{
								if (bg_worker.CancellationPending)
								{
									args.Cancel = true;
									return;
								}
								QueryTable(bg_worker, selectedItem.AddRow);
							};
							bg_worker.RunWorkerCompleted += (obj, args) =>
							{
								if (!bg_worker.CancellationPending)
								{
									pg_form.Close();
									Datapanel.Visible = true;
									Crosssectionpanel.Visible = false;
								}
								if (args.Cancelled)
								{
									Datapanel.Visible = true;
									Crosssectionpanel.Visible = false;
								}
							};

							bg_worker.WorkerSupportsCancellation = true;
							bg_worker.RunWorkerAsync();
						}
						catch (Exception)
						{
						}
						break;
					case "exe": //외부 프로그램 실행
						try
						{
							string batPath = selectedItem.Value;
							Process.Start(batPath);
						}
						catch (Exception)
						{
							MessageBox.Show("실행하려는 외부 프로그램이 설치되었는지 확인해주세요.");
						}
						break;
				}
			}
		}

		private class XmlData
		{
			public int Id { get; }
			public string Name { get; }
			public string Type { get; }
			public string Value { get; }
			public string AddRow { get; }
			public XmlData(int id, string name, string type, string value, string addrow)
			{
				Id = id;
				Name = name;
				Type = type;
				Value = value;
				AddRow = addrow;
			}
			public override string ToString()
			{
				return Name;
			}
		}

		//프로그램 정보 조회
		private void AppInfoMenu_Click(object sender, EventArgs e)
		{
			Open_AppInfoForm();
		}

		// 도움말 다운로드
		private void AppGuideMenu_Click(object sender, EventArgs e)
		{
			DownLoad_AppGuide();
		}

		// TXT 파일읽기 창 생성
		private void OpenImportForm()
		{
			ImportForm importForm;
			importForm = new ImportForm();
			importForm.FileContentRead += Import_InputVal;
			importForm.ShowDialog();
		}

		private void import_valueMenu_Click(object sender, EventArgs e)
		{
			OpenImportForm();
		}

		// TXT 파일 읽어 입력창 생성
		public void Import_InputVal(string path)
		{
			try
			{
				List<string> values = new List<string>();
				string[] lines = File.ReadAllLines(path);
				string tName = lines[0];
				for (int i = 1; i < lines.Length; i++)
				{
					string line = lines[i];
					values.Add(line);
				}
				if (tName == tableName)
				{
					Set_InputPanel(values);
				}
				else
				{
					MessageBox.Show("파일의 내용이 설정과 맞지 않습니다.\n다른 설정에서 입력을 시도하세요.");
				}

			}
			catch (Exception ex) { MessageBox.Show(ex.StackTrace); }
		}

		// TXT 파일 내용 입력창에 입력
		public void Set_InputPanel(List<string> values)
		{
			List<string> mokjas = new List<string> { "H" };
			List<string> tfs = new List<string> { "True", "False" };
			List<string> rateTypes = new List<string> { "측선유속측정", "표면유속측정" };
			List<string> traceUse = new List<string>() { "수면 추적 사용", "수면 추적 사용 안함" };
			List<string> gateTypes = new List<string> { "자동", "수동" };
			List<string> digits = new List<string> { "0", "1", "2", "3" };
			List<string> displayTypes = new List<string> { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };//new List<string> { "1", "5", "6", "7", "8" };//new List<string> { "0", "1", "2", "3", "4", "5", "6", "7", "8" };
			List<string> directions = new List<string> { "하 -> 상", "상 -> 하" };
			List<string> river_directions = new List<string> { "우 -> 좌", "좌 -> 우", "전체" };
			List<string> wateraiModes = new List<string> { "1", "2", "3", "4" };

			DataGridView tableQueryView = this.tableQueryView;

			try
			{
				if (this.input_panel.Controls.Count > 0)
				{
					this.input_panel.Controls.Clear();
				}

				for (int i = 0; i < values.Count; i++)
				{
					FlowLayoutPanel panel = new FlowLayoutPanel();
					Label label = new Label();
					TextBox textBox = new TextBox();
					ComboBox comboBox = new ComboBox();

					panel.FlowDirection = FlowDirection.LeftToRight;
					panel.AutoSize = true;

					label.MinimumSize = new Size(150, 25);
					label.TextAlign = ContentAlignment.MiddleRight;

					textBox.MinimumSize = new Size(150, 25);
					textBox.TextAlign = HorizontalAlignment.Left;

					comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
					comboBox.MinimumSize = new Size(150, 25);

					string en_field = tableQueryView.Columns[i + 1].DataPropertyName;
					var table = tables[tableIdx];
					string text = values[i];

					if (table.TryGetValue(en_field, out string kr_field))
					{
						label.Text = kr_field;
						label.Tag = en_field;
						textBox.Tag = kr_field;
						textBox.Text = text;
					}
					else
					{
						label.Text = en_field;
						textBox.ReadOnly = true;
						textBox.BackColor = SystemColors.ControlLightLight;
					}
					if (!en_field.Equals("No"))
					{
						panel.Controls.Add(label);
						panel.Controls.Add(textBox);

					}
					// 비밀번호
					if (en_field.Contains("pw") || en_field.Contains("password") || en_field.Contains("apikey"))
					{
						textBox.UseSystemPasswordChar = true;
					}
					//표기 자릿수
					if (en_field.Equals("measuredigits"))
					{
						textBox.Visible = false;
						foreach (string digit in digits)
						{
							comboBox.Items.Add(digit);
							comboBox.SelectedItem = (text == digit) ? digit : text;
						}
						panel.Controls.Add(comboBox);
					}
					//표기 방식
					if (en_field.Equals("display_type"))
					{
						textBox.Visible = false;
						foreach (string display in displayTypes)
						{
							comboBox.Items.Add(display);
							comboBox.SelectedItem = (text == display) ? display : text;
						}
						panel.Controls.Add(comboBox);
					}
					//waterai 모드
					if (en_field.Equals("waterai_mode"))
					{
						textBox.Visible = false;
						foreach (string wateraimode in wateraiModes)
						{
							comboBox.Items.Add(wateraimode);
							comboBox.SelectedItem = (text == wateraimode) ? wateraimode : text;
						}
						panel.Controls.Add(comboBox);
					}
					//목자타입
					if (en_field.Equals("mokjatype"))
					{
						textBox.Visible = false;
						foreach (string mokja in mokjas)
						{
							comboBox.Items.Add(mokja);
							comboBox.SelectedItem = (text == mokja) ? mokja : text;
						}
						comboBox.SelectedIndexChanged += (obj, args) =>
						{
							textBox.Text = comboBox.SelectedText;
						};
						panel.Controls.Add(comboBox);
					}
					//True/False
					if (en_field.Equals("isblur") || en_field.Equals("use_virtual") || en_field.Equals("auto_track") || en_field.Equals("use_flow_direction") || en_field.Equals("of_auto"))
					{

						textBox.Visible = false;
						foreach (string tf in tfs)
						{
							comboBox.Items.Add(tf);
							comboBox.SelectedItem = (text == tf) ? tf : text;
							textBox.Text = (text == "False") ? "0" : "1";

						}
						panel.Controls.Add(comboBox);
					}
					//영상 저장소
					if (en_field.Equals("video_save_path"))
					{
						textBox.Click += (sender, e) =>
						{
							FolderBrowserDialog d = new FolderBrowserDialog();
							if (d.ShowDialog() == DialogResult.OK)
							{
								string selectPath = d.SelectedPath;
								selectPath = selectPath.Substring(0, 1).ToLower() + selectPath.Substring(1);
								selectPath = selectPath.Replace(":", "");
								string replacePath = selectPath.Replace("\\", "/");

								textBox.Text = "/mnt/" + replacePath + "/";
							}
						};
					}
					//유속측정방식
					if (en_field.Equals("issiding"))
					{
						textBox.Visible = false;
						foreach (string rateType in rateTypes)
						{
							comboBox.Items.Add(rateType);
							comboBox.SelectedItem = (text == "측선유속측정") ? "측선유속측정" : "표면유속측정";
							textBox.Text = (text == "측선유속측정") ? "T" : "F";
						}
						panel.Controls.Add(comboBox);
					}
					//OF 자동
					if (en_field.Equals("of_auto"))
					{
						textBox.Visible = false;
						foreach (string tf in tfs)
						{
							comboBox.Items.Add(tf);
							comboBox.SelectedItem = (text == "True") ? "T" : "F";
							textBox.Text = (text == "True") ? "T" : "F";
						}
						panel.Controls.Add(comboBox);
					}
					//차단기 상태
					if (en_field.Equals("gate_status"))
					{
						textBox.Visible = false;
						foreach (string gateType in gateTypes)
						{
							comboBox.Items.Add(gateType);
							comboBox.SelectedItem = (text == gateType) ? gateType : text;
						}
						panel.Controls.Add(comboBox);
					}
					//측선 방향
					if (en_field.Equals("siding_direction"))
					{
						textBox.Visible = false;
						foreach (string direction in directions)
						{
							comboBox.Items.Add(direction);
							comboBox.SelectedItem = (text == "하 -> 상") ? "하 -> 상" : "상 -> 하";
							textBox.Text = (text == "하 -> 상") ? "1" : "0";
						}
						panel.Controls.Add(comboBox);
					}
					// 하천 방향
					if (en_field.Equals("river_flow_direction"))
					{
						textBox.Visible = false;
						foreach (string d in river_directions)
						{
							comboBox.Items.Add(d);
							comboBox.MinimumSize = new Size(150, 25);
							comboBox.SelectedItem = (text == "1") ? "좌 -> 우" : (text == "0") ? "우 -> 좌" : "전체";
						}
						panel.Controls.Add(comboBox);
					}
					// 수면 추적 사용
					if (en_field.Equals("use_surface_trace"))
					{
						textBox.Visible = false;
						foreach (string ust in traceUse)
						{
							comboBox.Items.Add(ust);
							comboBox.SelectedItem = (text == "0") ? traceUse[1] : traceUse[1];
						}
						panel.Controls.Add(comboBox);
					}
					input_panel.Controls.Add(panel);
				}

				if (tableIdx != 4)
				{
					label1.Text = "※ 데이터를 입력한 뒤 저장버튼을 눌러 저장하세요.";
					saveBtn.Enabled = true;
				}
				else
				{
					label1.Text = "※ 종합 정보 조회시에는 값을 수정 할 수 없습니다.";
					saveBtn.Enabled = false;
				}
			}
			catch (Exception e) { MessageBox.Show(e.StackTrace); }
		}

		public void Save_OriginalVideo()
		{
			try
			{
				Process.Start(saveOriginalVideoPath);
			}
			catch (Exception e)
			{
				MessageBox.Show(e.StackTrace);
			}
		}

		private void SaveOriginalVideoToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Save_OriginalVideo();
		}

		private void listBox_tables_SelectedIndexChanged(object sender, EventArgs e)
		{

		}

		//SFTP 접속정보 클래스
		public partial class SFTPHost
		{
			public string Host { get; set; }
			public int Port { get; set; }
			public string UserName { get; set; }
			public string Password { get; set; }
			public string Path { get; set; }

			public SFTPHost()
			{
				Host = "";
				UserName = "";
				Password = "";
				Path = "";
			}
		}


	}
}